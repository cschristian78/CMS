/** Encode binary to base64 in chunks (avoids call-stack limits on large files). */
function bytesToBase64(bytes: Uint8Array): string {
  const chunkSize = 0x8000;
  let binary = '';
  for (let i = 0; i < bytes.length; i += chunkSize) {
    const slice = bytes.subarray(i, i + chunkSize);
    binary += String.fromCharCode(...slice);
  }
  return btoa(binary);
}

function wrapBase64(value: string, lineLength = 76): string {
  const lines: string[] = [];
  for (let i = 0; i < value.length; i += lineLength) {
    lines.push(value.slice(i, i + lineLength));
  }
  return lines.join('\r\n');
}

function sanitizeHeaderValue(value: string): string {
  return value.replace(/[\r\n]+/g, ' ').trim();
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function stripExtension(fileName: string): string {
  const i = fileName.lastIndexOf('.');
  return i > 0 ? fileName.slice(0, i) : fileName;
}

function guessContentType(fileName: string, fallback?: string): string {
  if (fallback && fallback.trim()) {
    return fallback.trim();
  }
  const ext = fileName.includes('.') ? fileName.slice(fileName.lastIndexOf('.') + 1).toLowerCase() : '';
  switch (ext) {
    case 'pdf':
      return 'application/pdf';
    case 'xlsx':
      return 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
    case 'xls':
      return 'application/vnd.ms-excel';
    case 'docx':
      return 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
    case 'doc':
      return 'application/msword';
    case 'csv':
      return 'text/csv';
    case 'txt':
      return 'text/plain';
    case 'zip':
      return 'application/zip';
    case 'png':
      return 'image/png';
    case 'jpg':
    case 'jpeg':
      return 'image/jpeg';
    default:
      return 'application/octet-stream';
  }
}

/** Build a simple HTML body from plain-text lines, turning [label](url) markers into anchors. */
export function buildOutlookHtmlFromLines(lines: string[]): string {
  const htmlLines = lines.map((line) => {
    const linkMatch = line.match(/^\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)$/);
    if (linkMatch) {
      return `<p><a href="${escapeHtml(linkMatch[2])}">${escapeHtml(linkMatch[1])}</a></p>`;
    }
    const prefixed = line.match(/^(.+?):\s*\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)$/);
    if (prefixed) {
      return `<p>${escapeHtml(prefixed[1])}: <a href="${escapeHtml(prefixed[3])}">${escapeHtml(prefixed[2])}</a></p>`;
    }
    if (!line.trim()) {
      return '<p>&nbsp;</p>';
    }
    return `<p>${escapeHtml(line)}</p>`;
  });
  return `<html><body>${htmlLines.join('')}</body></html>`;
}

/**
 * Build an Outlook-friendly .eml draft (X-Unsent: 1).
 * Optional binary attachment; HTML body supports aliased links (mailto cannot).
 */
export async function openOutlookDraftWithAttachment(options: {
  subject: string;
  bodyText: string;
  bodyHtml?: string;
  ccEmails?: string[];
  toEmails?: string[];
  /** Used for the downloaded .eml file name (and default subject). */
  fileName?: string;
  /** Preferred generic attachment blob. */
  attachmentBlob?: Blob;
  /** @deprecated Prefer attachmentBlob. */
  pdfBlob?: Blob;
  attachmentFileName?: string;
  attachmentContentType?: string;
}): Promise<void> {
  const draftName = sanitizeHeaderValue(options.fileName || options.attachmentFileName || 'ProjectReport');
  const subject = sanitizeHeaderValue(options.subject || draftName);
  const cc = (options.ccEmails ?? []).map((e) => e.trim()).filter(Boolean);
  const to = (options.toEmails ?? []).map((e) => e.trim()).filter(Boolean);
  const bodyText = (options.bodyText || '').replace(/\r?\n/g, '\r\n');
  const bodyHtml = (options.bodyHtml || '').trim();
  const attachmentName = sanitizeHeaderValue(options.attachmentFileName || options.fileName || 'attachment.bin');
  const attachmentBlob = options.attachmentBlob ?? options.pdfBlob;
  const hasAttachment = !!attachmentBlob;
  const contentType = guessContentType(attachmentName, options.attachmentContentType || attachmentBlob?.type);

  const mixedBoundary = `----=_CMS_Mixed_${Date.now()}`;
  const altBoundary = `----=_CMS_Alt_${Date.now()}`;

  const headers = [
    to.length > 0 ? `To: ${to.join(', ')}` : 'To: ',
    cc.length > 0 ? `Cc: ${cc.join(', ')}` : null,
    `Subject: ${subject}`,
    'X-Unsent: 1',
    'MIME-Version: 1.0',
    hasAttachment || bodyHtml
      ? `Content-Type: multipart/mixed; boundary="${mixedBoundary}"`
      : 'Content-Type: text/plain; charset="utf-8"',
  ].filter((line): line is string => line != null);

  const parts: string[] = [...headers, ''];

  if (!hasAttachment && !bodyHtml) {
    parts.push(bodyText, '');
  } else {
    parts.push(`--${mixedBoundary}`);

    if (bodyHtml) {
      parts.push(
        `Content-Type: multipart/alternative; boundary="${altBoundary}"`,
        '',
        `--${altBoundary}`,
        'Content-Type: text/plain; charset="utf-8"',
        'Content-Transfer-Encoding: 7bit',
        '',
        bodyText,
        '',
        `--${altBoundary}`,
        'Content-Type: text/html; charset="utf-8"',
        'Content-Transfer-Encoding: 7bit',
        '',
        bodyHtml.replace(/\r?\n/g, '\r\n'),
        '',
        `--${altBoundary}--`,
        '',
      );
    } else {
      parts.push(
        'Content-Type: text/plain; charset="utf-8"',
        'Content-Transfer-Encoding: 7bit',
        '',
        bodyText,
        '',
      );
    }

    if (hasAttachment && attachmentBlob) {
      const bytes = new Uint8Array(await attachmentBlob.arrayBuffer());
      const b64 = wrapBase64(bytesToBase64(bytes));
      const safeAttachName = attachmentName.replace(/"/g, '');
      parts.push(
        `--${mixedBoundary}`,
        `Content-Type: ${contentType}; name="${safeAttachName}"`,
        'Content-Transfer-Encoding: base64',
        `Content-Disposition: attachment; filename="${safeAttachName}"`,
        '',
        b64,
        '',
      );
    }

    parts.push(`--${mixedBoundary}--`, '');
  }

  const blob = new Blob([parts.join('\r\n')], { type: 'message/rfc822' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  const emlBase = stripExtension(draftName);
  anchor.href = url;
  anchor.download = `${emlBase || 'EmailDraft'}.eml`;
  anchor.rel = 'noopener';
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
