import { useQuery } from '@tanstack/react-query'
import { ArrowLeftIcon, FileIcon, FolderIcon, LinkIcon } from 'lucide-react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { Page } from '@/components/page'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api } from '@/lib/api'
import { formatBytes, formatTime } from '@/lib/format'
import { useI18n } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'

/** The files of a plain storage object, folder by folder, read from its device. */
export function ObjectFilesPage() {
  const { groupId = '', objectId = '' } = useParams()
  const { t, locale } = useI18n()
  const notify = useNotify()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const path = params.get('path') ?? '/'

  const groups = useQuery({ queryKey: ['groups'], queryFn: api.groups.list })
  const devices = useQuery({ queryKey: ['devices'], queryFn: api.devices.list })
  const object = groups.data?.find((group) => group.id === groupId)?.objects.find((item) => item.id === objectId)
  const device = devices.data?.find((item) => item.id === object?.deviceId)

  const listing = useQuery({
    queryKey: ['object-files', groupId, objectId, path],
    queryFn: () => api.groups.objectFiles(groupId, objectId, path),
    // A disk can be unplugged or filled meanwhile; the list is read again when the page is shown.
    staleTime: 0,
  })

  const open = (next: string) => setParams(next === '/' ? {} : { path: next })
  const segments = path.split('/').filter(Boolean)

  return (
    <Page
      title={device && object ? `${device.name} · ${object.path}` : t('files.object.title')}
      description={t('files.object.title')}
      actions={
        <Button size="sm" variant="outline" onClick={() => void navigate('/groups')}>
          <ArrowLeftIcon />
          {t('files.object.back')}
        </Button>
      }
    >
      <div className="mx-auto max-w-5xl">
        <Card>
          <CardHeader>
            <CardDescription className="flex flex-wrap items-center gap-1 font-mono">
              <Button variant="ghost" size="xs" onClick={() => open('/')}>
                {t('files.root')}
              </Button>
              {segments.map((segment, index) => (
                <span key={segment + index} className="flex items-center gap-1">
                  /
                  <Button variant="ghost" size="xs" onClick={() => open('/' + segments.slice(0, index + 1).join('/'))}>
                    {segment}
                  </Button>
                </span>
              ))}
            </CardDescription>
          </CardHeader>
          <CardContent className="px-0">
            {listing.isPending && <Skeleton className="mx-4 h-24" />}
            {listing.isError && <p className="px-4 text-sm text-destructive">{notify.describe(listing.error)}</p>}
            {listing.data?.entries.length === 0 && (
              <p className="px-4 text-sm text-muted-foreground">{t('files.empty')}</p>
            )}
            {listing.data && listing.data.entries.length > 0 && (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-4">{t('files.col.name')}</TableHead>
                    <TableHead className="hidden sm:table-cell">{t('files.col.size')}</TableHead>
                    <TableHead className="hidden pr-4 md:table-cell">{t('files.col.modified')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {listing.data.entries.map((entry) => (
                    <TableRow
                      key={entry.path}
                      className={entry.type === 'dir' ? 'cursor-pointer' : undefined}
                      onClick={entry.type === 'dir' ? () => open(entry.path) : undefined}
                    >
                      <TableCell className="pl-4">
                        {entry.type === 'dir' ? (
                          <Button variant="ghost" size="sm" className="cursor-pointer">
                            <FolderIcon />
                            {entry.name}
                          </Button>
                        ) : (
                          <span
                            className="flex items-center gap-1.5 px-2.5 text-sm"
                            title={entry.type === 'symlink' ? t('files.type.symlink') : undefined}
                          >
                            {entry.type === 'symlink' ? (
                              <LinkIcon className="size-3.5 text-muted-foreground" />
                            ) : (
                              <FileIcon className="size-3.5 text-muted-foreground" />
                            )}
                            {entry.name}
                          </span>
                        )}
                      </TableCell>
                      <TableCell className="hidden text-muted-foreground sm:table-cell">
                        {entry.type === 'file' ? formatBytes(entry.size) : ''}
                      </TableCell>
                      <TableCell className="hidden pr-4 text-muted-foreground md:table-cell">
                        {formatTime(entry.modified, locale)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
            {listing.data?.truncated && (
              <p className="px-4 pt-2 text-xs text-muted-foreground">
                {t('files.object.truncated', { count: listing.data.entries.length })}
              </p>
            )}
          </CardContent>
        </Card>
      </div>
    </Page>
  )
}
