import { FormEvent, ReactNode, useCallback, useEffect, useMemo, useState } from 'react';
import { ApiError, apiDelete, apiGet, apiPostJson } from '../api';

type ChatRole = 'user' | 'assistant';

interface ChatMessage {
  role: ChatRole;
  content: string;
  toolsUsed?: string[];
}

interface CatAiApiResponse {
  reply: string;
  model?: string;
  conversationId?: number;
  toolsUsed?: string[];
  wizardActive?: boolean;
  wizardKind?: string | null;
  wizardLabel?: string | null;
}

interface ConversationSummary {
  conversationId: number;
  title: string;
  createdUtc: string;
  modifiedUtc: string;
  wizardActive?: boolean;
  wizardKind?: string | null;
}

interface StoredMessage {
  messageId: number;
  role: string;
  content: string;
  toolName?: string | null;
  createdUtc: string;
}

interface ConversationDetail {
  conversationId: number;
  title: string;
  wizardActive?: boolean;
  wizardKind?: string | null;
  messages: StoredMessage[];
}

const WELCOME: ChatMessage = {
  role: 'assistant',
  content:
    'Hi! I am CatAI. This chat remembers prior messages — use New chat when you want a clean slate. Ask about clients, projects, or user groups, or say you want to create a renewal project or user group.',
};

/** Markdown [label](url) or bare https URL — labels become the clickable text. */
const MESSAGE_LINK_TOKEN =
  /\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)|(https?:\/\/[^\s<>"'`]+)/gi;

function trimTrailingPunctuation(url: string): { href: string; trailing: string } {
  const href = url.replace(/[.,);:\]]+$/g, '');
  return { href, trailing: url.slice(href.length) };
}

function renderMessageContent(content: string) {
  const nodes: ReactNode[] = [];
  let lastIndex = 0;
  const re = new RegExp(MESSAGE_LINK_TOKEN.source, 'gi');
  let match: RegExpExecArray | null;
  let key = 0;

  while ((match = re.exec(content)) !== null) {
    if (match.index > lastIndex) {
      nodes.push(<span key={`t${key++}`}>{content.slice(lastIndex, match.index)}</span>);
    }

    if (match[1] && match[2]) {
      const label = match[1];
      const href = match[2];
      nodes.push(
        <a
          key={`a${key++}`}
          className="catai-link"
          href={href}
          target="_blank"
          rel="noopener noreferrer"
          title={href}
        >
          {label}
        </a>,
      );
    } else {
      const raw = match[3] || match[0];
      const { href, trailing } = trimTrailingPunctuation(raw);
      nodes.push(
        <a
          key={`a${key++}`}
          className="catai-link"
          href={href}
          target="_blank"
          rel="noopener noreferrer"
          title={href}
        >
          {href}
        </a>,
      );
      if (trailing) {
        nodes.push(<span key={`p${key++}`}>{trailing}</span>);
      }
    }

    lastIndex = match.index + match[0].length;
  }

  if (lastIndex < content.length) {
    nodes.push(<span key={`t${key++}`}>{content.slice(lastIndex)}</span>);
  }

  return nodes.length > 0 ? nodes : content;
}

function wizardLabelFromKind(kind?: string | null, active?: boolean): string | null {
  if (!active || !kind) {
    return null;
  }
  if (kind === 'renewal_project') {
    return 'Creating renewal project';
  }
  if (kind === 'user_group') {
    return 'Creating user group';
  }
  return 'Wizard in progress';
}

export default function CatAiPage() {
  const [messages, setMessages] = useState<ChatMessage[]>([WELCOME]);
  const [prompt, setPrompt] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conversationId, setConversationId] = useState<number | null>(null);
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [wizardActive, setWizardActive] = useState(false);
  const [wizardLabel, setWizardLabel] = useState<string | null>(null);

  const canSend = useMemo(() => prompt.trim().length > 0 && !sending, [prompt, sending]);

  const reloadConversations = useCallback(async () => {
    try {
      const list = await apiGet<ConversationSummary[]>('/api/catai/conversations');
      setConversations(Array.isArray(list) ? list : []);
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Could not load chat history.';
      setError(message);
    }
  }, []);

  useEffect(() => {
    void (async () => {
      setHistoryLoading(true);
      try {
        const list = await apiGet<ConversationSummary[]>('/api/catai/conversations');
        const rows = Array.isArray(list) ? list : [];
        setConversations(rows);
        if (rows.length > 0) {
          await openConversation(rows[0]!.conversationId, false);
        }
      } catch (err) {
        const message =
          err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Could not load chat history.';
        setError(message);
      } finally {
        setHistoryLoading(false);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- mount-only hydrate
  }, []);

  async function openConversation(id: number, refreshList: boolean) {
    setError(null);
    try {
      const detail = await apiGet<ConversationDetail>(`/api/catai/conversations/${id}`);
      setConversationId(detail.conversationId);
      setWizardActive(detail.wizardActive === true);
      setWizardLabel(wizardLabelFromKind(detail.wizardKind, detail.wizardActive === true));
      const mapped: ChatMessage[] = [];
      let pendingTools: string[] = [];
      for (const m of detail.messages ?? []) {
        const role = (m.role ?? '').toLowerCase();
        if (role === 'tool') {
          if (m.toolName) {
            pendingTools.push(m.toolName);
          }
          continue;
        }
        if (role === 'user') {
          mapped.push({ role: 'user', content: m.content });
          pendingTools = [];
          continue;
        }
        if (role === 'assistant') {
          mapped.push({
            role: 'assistant',
            content: m.content,
            toolsUsed: pendingTools.length > 0 ? [...pendingTools] : undefined,
          });
          pendingTools = [];
        }
      }
      setMessages(mapped.length > 0 ? mapped : [WELCOME]);
      if (refreshList) {
        await reloadConversations();
      }
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Could not open conversation.';
      setError(message);
    }
  }

  function startNewChat() {
    setConversationId(null);
    setMessages([WELCOME]);
    setPrompt('');
    setError(null);
    setWizardActive(false);
    setWizardLabel(null);
  }

  async function cancelWizard() {
    if (!conversationId) {
      setWizardActive(false);
      setWizardLabel(null);
      return;
    }
    try {
      await apiPostJson(`/api/catai/conversations/${conversationId}/cancel-wizard`, {});
      setWizardActive(false);
      setWizardLabel(null);
      setMessages((prev) => [
        ...prev,
        {
          role: 'assistant',
          content: 'Wizard cancelled. You can ask a new question anytime.',
        },
      ]);
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Could not cancel wizard.';
      setError(message);
    }
  }

  async function deleteConversation(id: number) {
    try {
      await apiDelete(`/api/catai/conversations/${id}`);
      if (conversationId === id) {
        startNewChat();
      }
      await reloadConversations();
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Could not delete conversation.';
      setError(message);
    }
  }

  async function onSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();

    const nextPrompt = prompt.trim();
    if (!nextPrompt || sending) {
      return;
    }

    const userMessage: ChatMessage = { role: 'user', content: nextPrompt };
    const isWelcomeOnly = messages.length === 1 && messages[0]?.content === WELCOME.content;
    const prior = isWelcomeOnly ? [] : messages;

    setMessages([...prior, userMessage]);
    setPrompt('');
    setSending(true);
    setError(null);

    try {
      // Server loads this conversation's history as model context (New chat clears it).
      const data = await apiPostJson<CatAiApiResponse>('/api/catai/chat', {
        conversationId: conversationId ?? undefined,
        messages: [{ role: 'user', content: nextPrompt }],
      });

      const answer = data.reply?.trim();
      if (!answer) {
        throw new Error('CatAI returned an empty response.');
      }

      if (typeof data.conversationId === 'number' && data.conversationId > 0) {
        setConversationId(data.conversationId);
      }

      setWizardActive(data.wizardActive === true);
      setWizardLabel(
        data.wizardLabel?.trim()
          || wizardLabelFromKind(data.wizardKind, data.wizardActive === true),
      );

      const toolsUsed = Array.isArray(data.toolsUsed) ? data.toolsUsed.filter(Boolean) : [];
      setMessages((prev) => [
        ...prev,
        {
          role: 'assistant',
          content: answer,
          toolsUsed: toolsUsed.length > 0 ? toolsUsed : undefined,
        },
      ]);
      await reloadConversations();
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Unable to reach CatAI right now.';
      setError(message);
      setMessages((prev) => [
        ...prev,
        {
          role: 'assistant',
          content: 'I hit an error while reaching the AI service. Please try again.',
        },
      ]);
    } finally {
      setSending(false);
    }
  }

  return (
    <>
      <section className="page-header">
        <h1>CatAI</h1>
        <p>
          Chat keeps prior messages in context. Use <strong>New chat</strong> to clear memory. Creating a renewal
          project or user group still uses a guided wizard.
        </p>
      </section>

      <section className="card wide-card catai-layout">
        <aside className="catai-history" aria-label="Chat history">
          <div className="catai-history-header">
            <strong>Chats</strong>
            <button type="button" className="secondary-button" onClick={startNewChat} disabled={sending}>
              New chat
            </button>
          </div>
          {historyLoading ? <p className="field-help">Loading…</p> : null}
          {!historyLoading && conversations.length === 0 ? (
            <p className="field-help">No saved chats yet.</p>
          ) : null}
          <ul className="catai-history-list">
            {conversations.map((c) => (
              <li key={c.conversationId} className={conversationId === c.conversationId ? 'active' : undefined}>
                <button
                  type="button"
                  className="catai-history-item"
                  onClick={() => void openConversation(c.conversationId, false)}
                  disabled={sending}
                >
                  <span className="catai-history-title">{c.title || 'New chat'}</span>
                  <span className="field-help">
                    {c.wizardActive ? 'Wizard · ' : ''}
                    {c.modifiedUtc ? new Date(c.modifiedUtc).toLocaleString() : ''}
                  </span>
                </button>
                <button
                  type="button"
                  className="catai-history-delete"
                  title="Delete chat"
                  aria-label={`Delete ${c.title || 'chat'}`}
                  onClick={() => void deleteConversation(c.conversationId)}
                  disabled={sending}
                >
                  ×
                </button>
              </li>
            ))}
          </ul>
        </aside>

        <div className="catai-main">
          {error ? <div className="alert alert-error">{error}</div> : null}

          {wizardActive ? (
            <div className="catai-wizard-banner" role="status">
              <div>
                <strong>Wizard active</strong>
                <p className="field-help" style={{ margin: '0.2rem 0 0' }}>
                  {wizardLabel || 'Multi-step create flow'} — answers stay in context until you finish or cancel.
                </p>
              </div>
              <button type="button" className="secondary-button" onClick={() => void cancelWizard()} disabled={sending}>
                Cancel wizard
              </button>
            </div>
          ) : null}

          <div className="catai-transcript">
            {messages.map((m, idx) => (
              <div key={idx} className="catai-bubble" data-role={m.role}>
                <strong>{m.role === 'user' ? 'You' : 'CatAI'}:</strong>
                <div style={{ whiteSpace: 'pre-wrap', marginTop: 4 }}>{renderMessageContent(m.content)}</div>
                {m.role === 'assistant' && m.toolsUsed && m.toolsUsed.length > 0 ? (
                  <p className="field-help catai-sources">Sources used: {m.toolsUsed.join(', ')}</p>
                ) : null}
              </div>
            ))}
            {sending ? <p className="field-help">CatAI is looking things up…</p> : null}
          </div>

          <form onSubmit={(e) => void onSubmit(e)} className="catai-composer">
            <label htmlFor="catai-prompt">Message</label>
            <textarea
              id="catai-prompt"
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              rows={4}
              placeholder={
                wizardActive
                  ? 'Answer the wizard question…'
                  : 'Ask a follow-up or a new question… (New chat clears context)'
              }
              disabled={sending}
            />
            <div className="catai-composer-actions">
              <span className="field-help">
                {wizardActive
                  ? 'Create wizard active — prior steps stay in context until you finish or cancel.'
                  : 'Conversation memory is on. Click New chat to start fresh.'}
              </span>
              <button className="primary-button" type="submit" disabled={!canSend}>
                {sending ? 'Sending…' : 'Send'}
              </button>
            </div>
          </form>
        </div>
      </section>
    </>
  );
}
