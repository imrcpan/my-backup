import { Navigate, Route, Routes } from 'react-router-dom'
import { AppSidebar } from '@/components/app-sidebar'
import { Page } from '@/components/page'
import { SidebarInset, SidebarProvider } from '@/components/ui/sidebar'
import { useI18n } from '@/lib/i18n'
import { DevicesPage } from '@/pages/DevicesPage'
import { GroupsPage } from '@/pages/GroupsPage'
import { Placeholder } from '@/pages/Placeholder'

function NotFound() {
  const { t } = useI18n()
  return (
    <Page title={t('app.name')}>
      <p className="text-sm text-muted-foreground">{t('common.notFound')}</p>
    </Page>
  )
}

export default function App() {
  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset className="min-w-0">
        <Routes>
          <Route path="/" element={<Navigate to="/groups" replace />} />
          <Route path="/groups" element={<GroupsPage />} />
          <Route path="/devices" element={<DevicesPage />} />
          <Route path="/runs" element={<Placeholder title="page.runs.title" description="page.runs.description" />} />
          <Route path="*" element={<NotFound />} />
        </Routes>
      </SidebarInset>
    </SidebarProvider>
  )
}
