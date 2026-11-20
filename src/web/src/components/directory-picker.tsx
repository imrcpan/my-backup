import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ArrowUpIcon, FolderIcon, FolderOpenIcon } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { ScrollArea } from '@/components/ui/scroll-area'
import { api } from '@/lib/api'
import { useI18n } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'

function parentOf(path: string): string {
  const parts = path.split('/').filter(Boolean)
  return '/' + parts.slice(0, -1).join('/')
}

/** Browses the folders of a device, to choose where a storage object goes. */
export function DirectoryPicker({
  deviceId,
  start,
  onPick,
}: {
  deviceId: string
  /** Where to begin: the path typed so far, if it is a place that exists. */
  start: string
  onPick: (path: string) => void
}) {
  const { t } = useI18n()
  const notify = useNotify()
  const [open, setOpen] = useState(false)
  const [path, setPath] = useState('/')

  const folders = useQuery({
    queryKey: ['directories', deviceId, path],
    queryFn: () => api.devices.directories(deviceId, path),
    enabled: open && deviceId !== '',
  })

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (next) setPath(start.startsWith('/') ? start.replace(/\/+$/, '') || '/' : '/')
      }}
    >
      <PopoverTrigger render={<Button type="button" variant="outline" disabled={deviceId === ''} />}>
        <FolderOpenIcon />
        {t('object.browse')}
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80 gap-2">
        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label={t('object.browse.up')}
            disabled={path === '/'}
            onClick={() => setPath(parentOf(path))}
          >
            <ArrowUpIcon />
          </Button>
          <span className="truncate font-mono text-xs" title={path}>
            {path}
          </span>
        </div>
        <ScrollArea className="h-48 rounded-md border">
          <div className="flex flex-col p-1">
            {folders.isPending && <p className="p-2 text-sm text-muted-foreground">{t('common.loading')}</p>}
            {folders.isError && <p className="p-2 text-sm text-destructive">{notify.describe(folders.error)}</p>}
            {folders.data?.length === 0 && (
              <p className="p-2 text-sm text-muted-foreground">{t('object.browse.empty')}</p>
            )}
            {folders.data?.map((folder) => (
              <Button
                key={folder.path}
                type="button"
                variant="ghost"
                size="sm"
                className="justify-start"
                onClick={() => setPath(folder.path)}
              >
                <FolderIcon />
                <span className="truncate">{folder.name}</span>
              </Button>
            ))}
          </div>
        </ScrollArea>
        <Button
          type="button"
          size="sm"
          onClick={() => {
            onPick(path)
            setOpen(false)
          }}
        >
          {t('object.browse.use')}
        </Button>
      </PopoverContent>
    </Popover>
  )
}
