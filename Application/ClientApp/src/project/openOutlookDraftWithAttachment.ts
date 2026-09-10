/** Encode binary to base64 in chunks (avoids call-stack limits on large PDFs). */
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

/**
 * Build an Outlook-friendly .eml draft (X-Unsent: 1) with a PDF attachment and trigger download.
 * Browsers cannot attach files via mailto:; opening the .eml in Outlook preserves the attachment.
 */
export async function openOutlookDraftWithAttachment(options: {
  subject: string;
  bodyText: string;
  ccEmails?: string[];
  toEmails?: string[];
  fileName: string;
  pdfBlob: Blob;
}): Promise<void> {
  const fileName = sanitizeHeaderValue(options.fileName || 'ProjectReport.pdf');
  const subject = sanitizeHeaderValue(options.subject || fileName);
  const cc = (options.ccEmails ?? []).map((e) => e.trim()).filter(Boolean);
  const to = (options.toEmails ?? []).map((e) => e.trim()).filter(Boolean);
  const bytes = new Uint8Array(await options.pdfBlob.arrayBuffer());
  const b64 = wrapBase64(bytesToBase64(bytes));
  const boundary = `----=_CMS_Report_${Date.now()}`;

  const headers = [
    to.length > 0 ? `To: ${to.join(', ')}` : 'To: ',
    cc.length > 0 ? `Cc: ${cc.join(', ')}` : null,
    `Subject: ${subject}`,
    'X-Unsent: 1',
    'MIME-Version: 1.0',
    `Content-Type: multipart/mixed; boundary="${boundary}"`,
  ].filter((line): line is string => line != null);

  const bodyText = (options.bodyText || '').replace(/\r?\n/g, '\r\n');
  const eml = [
    ...headers,
    '',
    `--${boundary}`,
    'Content-Type: text/plain; charset="utf-8"',
    'Content-Transfer-Encoding: 7bit',
    '',
    bodyText,
    '',
    `--${boundary}`,
    `Content-Type: application/pdf; name="${fileName.replace(/"/g, '')}"`,
    'Content-Transfer-Encoding: base64',
    `Content-Disposition: attachment; filename="${fileName.replace(/"/g, '')}"`,
    '',
    b64,
    '',
    `--${boundary}--`,
    '',
  ].join('\r\n');

  const blob = new Blob([eml], { type: 'message/rfc822' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  const emlName = fileName.toLowerCase().endsWith('.pdf')
    ? `${fileName.slice(0, -4)}.eml`
    : `${fileName}.eml`;
  anchor.href = url;
  anchor.download = emlName;
  anchor.rel = 'noopener';
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
