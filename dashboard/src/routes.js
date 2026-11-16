// Route metadata drives navigation, page titles, and summaries. Labels are i18n keys.
export const REPOSITORY_URL = 'https://github.com/jchristn/Keyframe';
export const APP_VERSION = '0.1.0';

export const NAV_GROUPS = [
  {
    id: 'top',
    labelKey: null,
    items: [{ id: 'home', path: '/dashboard/home', icon: 'home', labelKey: 'nav.home' }]
  },
  {
    id: 'analyze',
    labelKey: 'nav.groups.analyze',
    items: [
      { id: 'image', path: '/dashboard/analyze/image', icon: 'image', labelKey: 'nav.image' },
      { id: 'video', path: '/dashboard/analyze/video', icon: 'video', labelKey: 'nav.video' },
      { id: 'audio', path: '/dashboard/analyze/audio', icon: 'audio', labelKey: 'nav.audio' }
    ]
  },
  {
    id: 'operate',
    labelKey: 'nav.groups.operate',
    items: [
      { id: 'jobs', path: '/dashboard/jobs', icon: 'jobs', labelKey: 'nav.jobs' },
      { id: 'endpoints', path: '/dashboard/endpoints', icon: 'endpoints', labelKey: 'nav.endpoints' }
    ]
  },
  {
    id: 'observability',
    labelKey: 'nav.groups.observability',
    items: [
      { id: 'analytics', path: '/dashboard/analytics', icon: 'analytics', labelKey: 'nav.analytics', admin: true },
      { id: 'request-history', path: '/dashboard/request-history', icon: 'history', labelKey: 'nav.requestHistory' },
      { id: 'api-explorer', path: '/dashboard/api-explorer', icon: 'explorer', labelKey: 'nav.apiExplorer' }
    ]
  },
  {
    id: 'system',
    labelKey: 'nav.groups.system',
    items: [
      { id: 'settings', path: '/dashboard/settings', icon: 'settings', labelKey: 'nav.settings', admin: true },
      { id: 'server', path: '/dashboard/server', icon: 'server', labelKey: 'nav.server' }
    ]
  }
];
