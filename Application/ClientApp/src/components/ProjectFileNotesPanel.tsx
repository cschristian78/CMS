import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { formatCalendarDateDisplay } from '../utils/dateInput';

export type FileNoteKind =
  | 'FileComment'
  | 'FileApprovalRequest'
  | 'FileApproved'
  | 'FileChangesRequested';

export interface FileNoteMember {
  userID: number;
  userFullName: string;
  email?: string;
  userRoleName?: string;
}

export interface FileNoteRow {
  noteID: number;
  userID: number;
  userFullName?: string | null;
  noteDate?: string | null;
  noteType?: string | null;
  noteText?: string | null;
  isPinned: boolean;
  projectFileID?: number | null;
  projectFileName?: string | null;
  mentionedUserIDs?: string | null;
}

export interface FileNoteFileOption {
  projectFileID: number;
  originalFileName: string;
  approvalStatus?: string | null;
  approvalUpdatedByName?: string | null;
  approvalUpdatedDate?: string | null;
}

export interface FileNoteDraft {
  projectFileID: number | '';
  noteText: string;
  noteKind: FileNoteKind;
  isPinned: boolean;
  notifyCatTeam: boolean;
  notifyAll: boolean;
  mentionedUserIds: number[];
}

const FILE_NOTE_KINDS: { value: FileNoteKind; label: string }[] = [
  { value: 'FileComment', label: 'Comment' },
  { value: 'FileApprovalRequest', label: 'Approval request' },
  { value: 'FileApproved', label: 'Approved' },
  { value: 'FileChangesRequested', label: 'Changes requested' },
];

function normalizeKind(noteType?: string | null): FileNoteKind {
  const t = (noteType ?? '').trim();
  if (t === 'FileApprovalRequest') return 'FileApprovalRequest';
  if (t === 'FileApproved') return 'FileApproved';
  if (t === 'FileChangesRequested') return 'FileChangesRequested';
  return 'FileComment';
}

function kindLabel(noteType?: string | null): string {
  return FILE_NOTE_KINDS.find((k) => k.value === normalizeKind(noteType))?.label ?? 'Comment';
}

function approvalLabel(status?: string | null): string {
  switch ((status ?? '').trim()) {
    case 'PendingReview':
      return 'Pending review';
    case 'Approved':
      return 'Approved';
    case 'ChangesRequested':
      return 'Changes requested';
    default:
      return 'No status';
  }
}

function approvalClass(status?: string | null): string {
  switch ((status ?? '').trim()) {
    case 'PendingReview':
      return 'file-notes-approval--pending';
    case 'Approved':
      return 'file-notes-approval--approved';
    case 'ChangesRequested':
      return 'file-notes-approval--changes';
    default:
      return 'file-notes-approval--none';
  }
}

function initials(name?: string | null): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return `${parts[0][0] ?? ''}${parts[parts.length - 1][0] ?? ''}`.toUpperCase();
}

function parseMentionIds(raw?: string | null): number[] {
  if (!raw) return [];
  return raw
    .split(',')
    .map((p) => Number(p.trim()))
    .filter((n) => Number.isFinite(n) && n > 0);
}

interface Props {
  isEdit: boolean;
  projectFiles: FileNoteFileOption[];
  selectedFileId: number | '';
  onSelectFile: (fileId: number | '') => void;
  notes: FileNoteRow[];
  draft: FileNoteDraft;
  onDraftChange: (next: FileNoteDraft) => void;
  members: FileNoteMember[];
  currentUserId: number;
  busy?: boolean;
  onSubmit: () => void;
  onEdit: (note: FileNoteRow) => void;
  onDelete: (noteId: number) => void;
  onQuickApproval: (kind: FileNoteKind) => void;
  /** SignalR connection state for this project. */
  liveState?: 'connected' | 'reconnecting' | 'disconnected';
}

export default function ProjectFileNotesPanel({
  isEdit,
  projectFiles,
  selectedFileId,
  onSelectFile,
  notes,
  draft,
  onDraftChange,
  members,
  currentUserId,
  busy = false,
  onSubmit,
  onEdit,
  onDelete,
  onQuickApproval,
  liveState = 'disconnected',
}: Props) {
  const threadRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const [mentionQuery, setMentionQuery] = useState<string | null>(null);
  const [mentionIndex, setMentionIndex] = useState(0);

  const selectedFile =
    selectedFileId !== '' ? projectFiles.find((f) => f.projectFileID === selectedFileId) : undefined;

  const threadNotes = useMemo(() => {
    const fileId = selectedFileId === '' ? 0 : Number(selectedFileId);
    if (fileId <= 0) return [];
    return notes
      .filter((n) => Number(n.projectFileID ?? 0) === fileId)
      .slice()
      .sort((a, b) => {
        if (!!a.isPinned !== !!b.isPinned) return a.isPinned ? -1 : 1;
        const da = a.noteDate ?? '';
        const db = b.noteDate ?? '';
        if (da !== db) return da.localeCompare(db);
        return a.noteID - b.noteID;
      });
  }, [notes, selectedFileId]);

  useEffect(() => {
    const el = threadRef.current;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
  }, [threadNotes.length, selectedFileId]);

  const mentionSuggestions = useMemo(() => {
    if (mentionQuery === null) return [];
    const q = mentionQuery.trim().toLowerCase();
    return members
      .filter((m) => m.userID > 0)
      .filter((m) => !q || m.userFullName.toLowerCase().includes(q))
      .slice(0, 8);
  }, [members, mentionQuery]);

  const updateTextAndMentions = (text: string) => {
    const at = text.lastIndexOf('@');
    if (at >= 0) {
      const after = text.slice(at + 1);
      if (!after.includes(' ') && !after.includes('\n')) {
        setMentionQuery(after);
        setMentionIndex(0);
      } else {
        setMentionQuery(null);
      }
    } else {
      setMentionQuery(null);
    }
    onDraftChange({ ...draft, noteText: text });
  };

  const insertMention = (member: FileNoteMember) => {
    const text = draft.noteText;
    const at = text.lastIndexOf('@');
    const prefix = at >= 0 ? text.slice(0, at) : text;
    const nextText = `${prefix}@${member.userFullName} `;
    const mentioned = draft.mentionedUserIds.includes(member.userID)
      ? draft.mentionedUserIds
      : [...draft.mentionedUserIds, member.userID];
    onDraftChange({ ...draft, noteText: nextText, mentionedUserIds: mentioned });
    setMentionQuery(null);
    textareaRef.current?.focus();
  };

  const onTextKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (mentionQuery === null || mentionSuggestions.length === 0) return;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setMentionIndex((i) => (i + 1) % mentionSuggestions.length);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setMentionIndex((i) => (i - 1 + mentionSuggestions.length) % mentionSuggestions.length);
    } else if (e.key === 'Enter' || e.key === 'Tab') {
      e.preventDefault();
      insertMention(mentionSuggestions[mentionIndex] ?? mentionSuggestions[0]);
    } else if (e.key === 'Escape') {
      setMentionQuery(null);
    }
  };

  const canCompose = isEdit && selectedFileId !== '' && !busy;

  return (
    <div className="file-notes-panel">
      <p className="field-help">
        Team discussion for a single project data file. Use Notify Cat Team or Notify All to email teammates;
        type @ to mention someone. Approval kinds update the file&apos;s approval status.{' '}
        <span
          className={
            'file-notes-live' +
            (liveState === 'connected'
              ? ' file-notes-live--on'
              : liveState === 'reconnecting'
                ? ' file-notes-live--reconnecting'
                : ' file-notes-live--off')
          }
          role="status"
        >
          {liveState === 'connected'
            ? 'Live updates connected'
            : liveState === 'reconnecting'
              ? 'Live updates reconnecting'
              : 'Live updates disconnected'}
        </span>
      </p>

      {!isEdit ? (
        <p className="field-help" role="status">
          Save the project and upload files on Data List before adding file notes.
        </p>
      ) : null}
      {isEdit && projectFiles.length === 0 ? (
        <p className="field-help" role="status">
          No uploaded files yet. Add files on the Data List subtab first.
        </p>
      ) : null}

      <div className="file-notes-layout">
        <div className="file-notes-main">
          <div className="form-grid file-notes-file-row">
            <label htmlFor="fileNoteFile">File</label>
            <select
              id="fileNoteFile"
              value={selectedFileId === '' ? '' : String(selectedFileId)}
              disabled={!isEdit || projectFiles.length === 0 || busy}
              onChange={(e) => {
                const next = e.target.value ? Number(e.target.value) : '';
                onSelectFile(next === '' || !Number.isFinite(next) ? '' : next);
              }}
            >
              <option value="">-- Select file --</option>
              {projectFiles.map((f) => (
                <option key={f.projectFileID} value={f.projectFileID}>
                  {f.originalFileName}
                </option>
              ))}
            </select>
          </div>

          {selectedFile ? (
            <div className={`file-notes-approval ${approvalClass(selectedFile.approvalStatus)}`}>
              <div className="file-notes-approval-status">
                <strong>Approval:</strong> {approvalLabel(selectedFile.approvalStatus)}
                {selectedFile.approvalUpdatedByName || selectedFile.approvalUpdatedDate ? (
                  <span className="file-notes-approval-meta">
                    {' '}
                    — {(selectedFile.approvalUpdatedByName ?? '').trim()}
                    {selectedFile.approvalUpdatedDate
                      ? ` · ${formatCalendarDateDisplay(selectedFile.approvalUpdatedDate)}`
                      : ''}
                  </span>
                ) : null}
              </div>
              {canCompose ? (
                <div className="file-notes-approval-actions">
                  <button
                    type="button"
                    className="secondary-button"
                    disabled={busy}
                    onClick={() => onQuickApproval('FileApprovalRequest')}
                  >
                    Request review
                  </button>
                  <button
                    type="button"
                    className="secondary-button"
                    disabled={busy}
                    onClick={() => onQuickApproval('FileApproved')}
                  >
                    Approve
                  </button>
                  <button
                    type="button"
                    className="secondary-button"
                    disabled={busy}
                    onClick={() => onQuickApproval('FileChangesRequested')}
                  >
                    Request changes
                  </button>
                </div>
              ) : null}
            </div>
          ) : null}

          <div className="file-notes-thread" ref={threadRef} role="log" aria-live="polite">
            {selectedFileId === '' ? (
              <p className="field-help">Select a file to view its conversation.</p>
            ) : threadNotes.length === 0 ? (
              <p className="field-help">No notes yet for this file. Start the discussion below.</p>
            ) : (
              threadNotes.map((n) => {
                const mine = currentUserId > 0 && n.userID === currentUserId;
                const name = (n.userFullName ?? '').trim() || (mine ? 'You' : 'Teammate');
                return (
                  <article
                    key={n.noteID}
                    className={
                      'file-notes-bubble' +
                      (mine ? ' file-notes-bubble--mine' : '') +
                      (n.isPinned ? ' file-notes-bubble--pinned' : '')
                    }
                  >
                    <div className="file-notes-bubble-avatar" aria-hidden="true">
                      {initials(name)}
                    </div>
                    <div className="file-notes-bubble-body">
                      <header className="file-notes-bubble-header">
                        <strong>{mine ? 'You' : name}</strong>
                        <span className="file-notes-bubble-kind">{kindLabel(n.noteType)}</span>
                        {n.isPinned ? <span className="file-notes-bubble-pin">Pinned</span> : null}
                        <span className="file-notes-bubble-date">
                          {formatCalendarDateDisplay(n.noteDate)}
                        </span>
                      </header>
                      <p className="file-notes-bubble-text">{n.noteText ?? ''}</p>
                      {parseMentionIds(n.mentionedUserIDs).length > 0 ? (
                        <p className="file-notes-bubble-mentions">
                          Mentions:{' '}
                          {parseMentionIds(n.mentionedUserIDs)
                            .map(
                              (id) =>
                                members.find((m) => m.userID === id)?.userFullName ?? `User ${id}`,
                            )
                            .join(', ')}
                        </p>
                      ) : null}
                      <div className="file-notes-bubble-actions">
                        <button type="button" className="secondary-button" onClick={() => onEdit(n)}>
                          Edit
                        </button>
                        <button
                          type="button"
                          className="danger-button"
                          onClick={() => onDelete(n.noteID)}
                        >
                          Delete
                        </button>
                      </div>
                    </div>
                  </article>
                );
              })
            )}
          </div>

          <div className="file-notes-composer">
            <div className="form-grid">
              <label htmlFor="fileNoteKind">Type</label>
              <select
                id="fileNoteKind"
                value={draft.noteKind}
                disabled={!canCompose}
                onChange={(e) =>
                  onDraftChange({ ...draft, noteKind: e.target.value as FileNoteKind })
                }
              >
                {FILE_NOTE_KINDS.map((k) => (
                  <option key={k.value} value={k.value}>
                    {k.label}
                  </option>
                ))}
              </select>

              <label htmlFor="fileNoteText">Message</label>
              <div className="file-notes-composer-text">
                <textarea
                  id="fileNoteText"
                  ref={textareaRef}
                  value={draft.noteText}
                  rows={3}
                  disabled={!canCompose}
                  onChange={(e) => updateTextAndMentions(e.target.value)}
                  onKeyDown={onTextKeyDown}
                  placeholder="Write a comment… Type @ to mention a teammate"
                />
                {mentionQuery !== null && mentionSuggestions.length > 0 ? (
                  <ul className="file-notes-mention-menu" role="listbox">
                    {mentionSuggestions.map((m, i) => (
                      <li key={m.userID}>
                        <button
                          type="button"
                          className={
                            'file-notes-mention-item' +
                            (i === mentionIndex ? ' file-notes-mention-item--active' : '')
                          }
                          onMouseDown={(e) => {
                            e.preventDefault();
                            insertMention(m);
                          }}
                        >
                          {m.userFullName}
                          {m.userRoleName ? ` · ${m.userRoleName}` : ''}
                        </button>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </div>

              <span className="field-label">Options</span>
              <div className="file-notes-composer-options">
                <label className="project-notes-pinned-label">
                  <input
                    type="checkbox"
                    checked={draft.isPinned}
                    disabled={!canCompose}
                    onChange={(e) => onDraftChange({ ...draft, isPinned: e.target.checked })}
                  />
                  Pin
                </label>
                <label className="project-notes-pinned-label">
                  <input
                    type="checkbox"
                    checked={draft.notifyCatTeam}
                    disabled={!canCompose || members.length === 0}
                    onChange={(e) => {
                      const checked = e.target.checked;
                      onDraftChange({
                        ...draft,
                        notifyCatTeam: checked,
                        // Prefer a single audience when toggling Cat Team on.
                        notifyAll: checked ? false : draft.notifyAll,
                      });
                    }}
                  />
                  Notify Cat Team
                </label>
                <label className="project-notes-pinned-label">
                  <input
                    type="checkbox"
                    checked={draft.notifyAll}
                    disabled={!canCompose || members.length === 0}
                    onChange={(e) => {
                      const checked = e.target.checked;
                      onDraftChange({
                        ...draft,
                        notifyAll: checked,
                        notifyCatTeam: checked ? false : draft.notifyCatTeam,
                      });
                    }}
                  />
                  Notify All
                </label>
              </div>

              <span className="field-label" aria-hidden="true" />
              <div className="page-header-actions project-services-select-actions">
                <button
                  type="button"
                  className="secondary-button"
                  disabled={!canCompose || !draft.noteText.trim()}
                  onClick={onSubmit}
                >
                  Post note
                </button>
              </div>
            </div>
          </div>
        </div>

        <aside className="file-notes-roster" aria-label="Project user group">
          <h3 className="file-notes-roster-title">User group</h3>
          {members.length === 0 ? (
            <p className="field-help">
              Assign a User Group on Project Info to show teammates and enable notify / mentions.
            </p>
          ) : (
            <ul className="file-notes-roster-list">
              {members.map((m) => (
                <li key={m.userID}>
                  <span className="file-notes-roster-avatar" aria-hidden="true">
                    {initials(m.userFullName)}
                  </span>
                  <span className="file-notes-roster-meta">
                    <strong>{m.userFullName}</strong>
                    {m.userRoleName ? <span>{m.userRoleName}</span> : null}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </aside>
      </div>
    </div>
  );
}
