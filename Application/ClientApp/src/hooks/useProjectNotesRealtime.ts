import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  ILogger,
  LogLevel,
} from '@microsoft/signalr';
import { useEffect, useRef, useState } from 'react';

export type RealtimeConnectionState = 'connected' | 'reconnecting' | 'disconnected';

export type ProjectNoteChange = {
  kind?: string;
  projectId: number;
  changeType: string;
  noteId?: number | null;
  projectFileId?: number | null;
};

type Options = {
  projectId: number | undefined;
  enabled: boolean;
  onNoteChanged: (change: ProjectNoteChange) => void;
  /** Used only while the hub is disconnected. */
  pollIntervalMs?: number;
  onPoll?: () => void;
};

/** Logs Information+ to the console and records the negotiated transport. Does not log credentials. */
function createTransportLogger(onTransport: (name: string) => void): ILogger {
  return {
    log(level, message) {
      if (level < LogLevel.Information) {
        return;
      }
      console.info(`[SignalR] ${message}`);
      const match = /using ([A-Za-z]+) transport/i.exec(message);
      if (match?.[1]) {
        onTransport(match[1]);
      }
    },
  };
}

/**
 * Same-origin SignalR connection. Transport is negotiated (WebSockets, Server-Sent Events,
 * or long polling). skipNegotiation is not set, so fallback works without the IIS WebSocket feature.
 */
export function useProjectNotesRealtime({
  projectId,
  enabled,
  onNoteChanged,
  pollIntervalMs = 8000,
  onPoll,
}: Options): { state: RealtimeConnectionState } {
  const [state, setState] = useState<RealtimeConnectionState>('disconnected');
  const connectionRef = useRef<HubConnection | null>(null);
  const onNoteChangedRef = useRef(onNoteChanged);
  const onPollRef = useRef(onPoll);

  onNoteChangedRef.current = onNoteChanged;
  onPollRef.current = onPoll;

  useEffect(() => {
    if (!enabled || projectId === undefined || projectId <= 0) {
      setState('disconnected');
      return;
    }

    let cancelled = false;
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/updates', { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(createTransportLogger((name) => {
        console.info(`[SignalR] transport in use: ${name}`);
      }))
      .build();

    connectionRef.current = connection;

    connection.on('update', (payload: ProjectNoteChange) => {
      if (!payload || Number(payload.projectId) !== projectId) {
        return;
      }
      if (payload.kind && payload.kind !== 'projectNote') {
        return;
      }
      onNoteChangedRef.current(payload);
    });

    connection.onreconnecting(() => {
      if (!cancelled) {
        setState('reconnecting');
      }
    });
    connection.onreconnected(() => {
      if (!cancelled) {
        setState('connected');
      }
      void connection.invoke('JoinProject', projectId).catch((err: unknown) => {
        console.info('[SignalR] JoinProject failed after reconnect.', err instanceof Error ? err.message : '');
      });
    });
    connection.onclose(() => {
      if (!cancelled) {
        setState('disconnected');
      }
    });

    void (async () => {
      try {
        if (!cancelled) {
          setState('reconnecting');
        }
        await connection.start();
        if (cancelled) {
          return;
        }
        await connection.invoke('JoinProject', projectId);
        if (!cancelled) {
          setState('connected');
        }
      } catch (err) {
        console.info('[SignalR] connection failed.', err instanceof Error ? err.message : '');
        if (!cancelled) {
          setState('disconnected');
        }
      }
    })();

    return () => {
      cancelled = true;
      setState('disconnected');
      const current = connectionRef.current;
      connectionRef.current = null;
      if (current && current.state !== HubConnectionState.Disconnected) {
        void current
          .invoke('LeaveProject', projectId)
          .catch(() => undefined)
          .finally(() => {
            void current.stop().catch(() => undefined);
          });
      }
    };
  }, [enabled, projectId]);

  useEffect(() => {
    if (!enabled || projectId === undefined || projectId <= 0 || !onPoll) {
      return;
    }

    const id = window.setInterval(() => {
      if (connectionRef.current?.state === HubConnectionState.Connected) {
        return;
      }
      onPollRef.current?.();
    }, Math.max(3000, pollIntervalMs));

    return () => window.clearInterval(id);
  }, [enabled, projectId, pollIntervalMs, onPoll]);

  return { state };
}
