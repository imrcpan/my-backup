import { useEffect, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { api } from '@/lib/api'
import { useI18n } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'

function stamp(): string {
  return new Date().toISOString().slice(0, 16).replace(/[-:]/g, '').replace('T', '-')
}

/** Asks where to restore to, which is a new folder by default, and makes the plan of the restore. */
export function RestoreDialog({
  open,
  onOpenChange,
  groupId,
  objectId,
  snapshotId,
  paths,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  groupId: string
  objectId: string
  snapshotId: string
  /** What to restore; the whole snapshot if empty. */
  paths: string[]
}) {
  const { t } = useI18n()
  const notify = useNotify()
  const navigate = useNavigate()
  const system = useQuery({ queryKey: ['system'], queryFn: api.system })
  const [target, setTarget] = useState('')

  useEffect(() => {
    if (open) setTarget(`${system.data?.home ?? ''}/restored-${stamp()}`)
  }, [open, system.data?.home])

  const plan = useMutation({
    mutationFn: () => api.runs.planRestore(groupId, objectId, snapshotId, target, paths.length > 0 ? paths : undefined),
    onSuccess: (run) => {
      onOpenChange(false)
      void navigate(`/runs/${run.id}`)
    },
    onError: notify.error,
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t('restore.title')}</DialogTitle>
          <DialogDescription>{t('restore.description')}</DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-4">
          <p className="text-sm text-muted-foreground">
            {t('restore.what')}: {paths.length === 0 ? t('restore.whole') : t('restore.items', { count: paths.length })}
          </p>
          <Field>
            <FieldLabel htmlFor="restore-target">{t('restore.target')}</FieldLabel>
            <Input
              id="restore-target"
              className="font-mono"
              value={target}
              autoComplete="off"
              onChange={(event) => setTarget(event.target.value)}
            />
            <FieldDescription>{t('restore.target.hint')}</FieldDescription>
          </Field>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t('common.cancel')}
          </Button>
          <Button disabled={plan.isPending} onClick={() => plan.mutate()}>
            {t('restore.plan')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
