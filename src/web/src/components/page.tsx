import { SidebarTrigger } from '@/components/ui/sidebar'
import { Separator } from '@/components/ui/separator'
import { Preferences } from '@/components/preferences'

/** The frame of a page: the header with the title, the actions of the page and the preferences. */
export function Page({
  title,
  description,
  actions,
  children,
}: {
  title: string
  description?: string
  actions?: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <>
      <header className="flex h-14 shrink-0 items-center gap-2 border-b px-4">
        <SidebarTrigger />
        <Separator orientation="vertical" className="mx-1 h-4" />
        <div className="flex min-w-0 flex-col">
          <h1 className="truncate text-sm font-semibold">{title}</h1>
          {description && <p className="hidden truncate text-xs text-muted-foreground sm:block">{description}</p>}
        </div>
        <div className="ml-auto flex items-center gap-2">
          {actions}
          <Preferences />
        </div>
      </header>
      <main className="flex-1 overflow-auto p-4 md:p-6">{children}</main>
    </>
  )
}
