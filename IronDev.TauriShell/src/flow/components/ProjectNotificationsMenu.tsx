import { useCallback, useEffect, useState } from 'react';
import type { ProjectNotificationListResponse } from '../../api/types';
import { useSessionContext } from '../../state/useSessionContext';

interface ProjectNotificationsMenuProps {
  projectId: number;
  onOpenChannel: (slug: string) => void;
}

export function ProjectNotificationsMenu({ projectId, onOpenChannel }: ProjectNotificationsMenuProps) {
  const session = useSessionContext();
  const [inbox, setInbox] = useState<ProjectNotificationListResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    try {
      setInbox(await session.client.getProjectNotifications(projectId, signal));
      setError(null);
    } catch {
      if (!signal?.aborted) setError('Notifications unavailable');
    }
  }, [projectId, session.client]);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);
    const interval = window.setInterval(() => void refresh(controller.signal), 30_000);
    return () => {
      controller.abort();
      window.clearInterval(interval);
    };
  }, [refresh]);

  const openNotification = async (notificationId: number, channelSlug: string | null) => {
    try {
      await session.client.markProjectNotificationRead(projectId, notificationId);
      setInbox((current) => current ? {
        ...current,
        unreadCount: Math.max(0, current.unreadCount - (current.notifications.some((item) => item.notificationId === notificationId && !item.isRead) ? 1 : 0)),
        notifications: current.notifications.map((item) =>
          item.notificationId === notificationId ? { ...item, isRead: true } : item
        )
      } : current);
      if (channelSlug) onOpenChannel(channelSlug);
    } catch {
      setError('Notification state could not be updated');
    }
  };

  return (
    <details className="fl-header-menu fl-notifications" onToggle={(event) => {
      if (event.currentTarget.open) void refresh();
    }}>
      <summary data-testid="flow.notifications">
        Notifications
        {inbox && inbox.unreadCount > 0 ? <span className="fl-notification-count">{inbox.unreadCount}</span> : null}
      </summary>
      <div className="fl-header-popover fl-notification-popover" aria-label="Project notifications">
        <header>
          <strong>Notifications</strong>
          <span>{inbox?.unreadCount ?? 0} unread</span>
        </header>
        {error ? <p className="state-error">{error}</p> : null}
        {inbox && inbox.notifications.length === 0 ? <p>No notifications.</p> : null}
        {inbox?.notifications.map((notification) => (
          <button
            key={notification.notificationId}
            type="button"
            className={notification.isRead ? '' : 'fl-notification-unread'}
            data-testid={`flow.notification.${notification.notificationId}`}
            onClick={() => void openNotification(notification.notificationId, notification.channelSlug)}
          >
            <strong>{notification.title}</strong>
            <span>{notification.body}</span>
          </button>
        ))}
      </div>
    </details>
  );
}
