import { FormEvent, useMemo, useState } from 'react';
import { ApiError, apiPostJson } from '../api';

type ChatRole = 'user' | 'assistant';

interface ChatMessage {
  role: ChatRole;
  content: string;
}

interface CatAiApiResponse {
  reply: string;
}

export default function CatAiPage() {
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      role: 'assistant',
      content: 'Hi! I am CatAI. Ask me anything about your work, projects, or clients.',
    },
  ]);
  const [prompt, setPrompt] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const canSend = useMemo(() => {
    return prompt.trim().length > 0 && !sending;
  }, [prompt, sending]);

  async function onSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();

    const nextPrompt = prompt.trim();
    if (!nextPrompt || sending) {
      return;
    }

    const userMessage: ChatMessage = { role: 'user', content: nextPrompt };
    const nextMessages = [...messages, userMessage];
    setMessages(nextMessages);
    setPrompt('');
    setSending(true);
    setError(null);

    try {
      const data = await apiPostJson<CatAiApiResponse>('/api/catai/chat', {
        messages: nextMessages.map((m) => ({
          role: m.role,
          content: m.content,
        })),
      });

      const answer = data.reply?.trim();
      if (!answer) {
        throw new Error('CatAI returned an empty response.');
      }

      setMessages((prev) => [...prev, { role: 'assistant', content: answer }]);
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
        <p>ChatGPT-style assistant backed by server-side OpenAI credentials.</p>
      </section>

      <section className="card wide-card">
        {error ? <div className="alert alert-error">{error}</div> : null}

        <div style={{ display: 'grid', gap: 12 }}>
          <div
            style={{
              border: '1px solid #d0d7de',
              borderRadius: 8,
              padding: 12,
              minHeight: 360,
              maxHeight: 560,
              overflowY: 'auto',
              background: '#fff',
            }}
          >
            {messages.map((m, idx) => (
              <div key={idx} style={{ marginBottom: 12 }}>
                <strong>{m.role === 'user' ? 'You' : 'CatAI'}:</strong>
                <div style={{ whiteSpace: 'pre-wrap', marginTop: 4 }}>{m.content}</div>
              </div>
            ))}
            {sending ? <p className="field-help">CatAI is typing...</p> : null}
          </div>

          <form onSubmit={onSubmit} style={{ display: 'grid', gap: 8 }}>
            <label htmlFor="catai-prompt">Message</label>
            <textarea
              id="catai-prompt"
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              rows={5}
              placeholder="Ask CatAI anything..."
              disabled={sending}
            />
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span className="field-help">Model is selected by server configuration.</span>
              <button className="primary-button" type="submit" disabled={!canSend}>
                {sending ? 'Sending...' : 'Send'}
              </button>
            </div>
          </form>
        </div>
      </section>
    </>
  );
}
