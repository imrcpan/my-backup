import { HardDriveIcon, HistoryIcon, LayersIcon } from 'lucide-react'
import { NavLink, useLocation } from 'react-router-dom'
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupContent,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarRail,
} from '@/components/ui/sidebar'
import { useI18n, type MessageKey } from '@/lib/i18n'

const items: { to: string; label: MessageKey; icon: typeof LayersIcon }[] = [
  { to: '/groups', label: 'nav.groups', icon: LayersIcon },
  { to: '/devices', label: 'nav.devices', icon: HardDriveIcon },
  { to: '/runs', label: 'nav.runs', icon: HistoryIcon },
]

export function AppSidebar() {
  const { t } = useI18n()
  const { pathname } = useLocation()

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
