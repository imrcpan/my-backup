import type { en } from './en'

export const zh: Record<keyof typeof en, string> = {
  'app.name': '备份管理器',
  'app.tagline': 'Restic 与 rclone，每次改动前先出计划',
  'nav.groups': '备份组',
  'nav.devices': '存储设备',
  'nav.runs': '执行记录',
  'theme.light': '亮色',
  'theme.dark': '暗色',
  'theme.system': '跟随系统',
  'theme.toggle': '切换主题',
  'language.toggle': '切换语言',
  'page.devices.title': '存储设备',
  'page.devices.description': '数据可以放在哪里：本机、外接硬盘和云盘。',
  'page.groups.title': '备份组',
  'page.groups.description': '一组彼此保持同步的存储对象。',
  'page.runs.title': '执行记录',
  'page.runs.description': '每一次计划和执行，包括进度和日志。',
  'common.coming': '这个页面还没有做。',
  'common.notFound': '这个页面不存在。',
}
