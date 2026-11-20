import { useQuery } from '@tanstack/react-query'
import { HardDriveIcon, HistoryIcon, LayersIcon, Loader2Icon } from 'lucide-react'
import { NavLink, useLocation } from 'react-router-dom'
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupContent,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuBadge,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarRail,
} from '@/components/ui/sidebar'
import { api } from '@/lib/api'
import { useI18n, type MessageKey } from '@/lib/i18n'

const items: { to: string; label: MessageKey; icon: typeof LayersIcon }[] = [
  { to: '/groups', label: 'nav.groups', icon: LayersIcon },
  { to: '/devices', label: 'nav.devices', icon: HardDriveIcon },
  { to: '/runs', label: 'nav.runs', icon: HistoryIcon },
]

export function AppSidebar() {
  const { t } = useI18n()
  const { pathname } = useLocation()
  const runs = useQuery({
    queryKey: ['runs'],
    queryFn: api.runs.list,
    refetchInterval: (query) => (query.state.data?.some((run) => run.state === 'Running') ? 2000 : 15000),
  })
  const running = runs.data?.some((run) => run.state === 'Running') ?? false

  return (
    <Sidebar>
      <SidebarHeader>
        <div className="flex flex-col px-2 py-1">
          <span className="font-heading text-sm font-semibold">{t('app.name')}</span>
          <span className="text-xs text-muted-foreground">{t('app.tagline')}</span>
        </div>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupContent>
            <SidebarMenu>
              {items.map((item) => (
                <SidebarMenuItem key={item.to}>
                  <SidebarMenuButton isActive={pathname.startsWith(item.to)} render={<NavLink to={item.to} />}>
                    <item.icon />
                    <span>{t(item.label)}</span>
                  </SidebarMenuButton>
                  {item.to === '/runs' && running && (
                    <SidebarMenuBadge title={t('nav.runs.active')}>
                      <Loader2Icon className="size-3.5 animate-spin" />
                    </SidebarMenuBadge>
                  )}
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>
      <SidebarRail />
    </Sidebar>
  )
}
