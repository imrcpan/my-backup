import { useEffect, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
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
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { api } from '@/lib/api'
import { useI18n, type MessageKey } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'
import type { CheckDepth } from '@/lib/types'

const depths: CheckDepth[] = ['Structure', 'Subset', 'Full']

/** Chooses how deep to check a repository and makes the plan of the check. */
export function CheckDialog({
  open,
  onOpenChange,
  groupId,
  objectId,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  groupId: string
  objectId: string
}) {
  const { t } = useI18n()
  const notify = useNotify()
  const navigate = useNavigate()
  const [depth, setDepth] = useState<CheckDepth>('Structure')
  const [percent, setPercent] = useState('10')

  useEffect(() => {
    if (open) {
      setDepth('Structure')
      setPercent('10')
    }
  }, [open])

  const plan = useMutation({
    mutationFn: () => api.runs.planCheck(groupId, objectId, depth, Number(percent) || 10),
    onSuccess: (run) => {
      onOpenChange(false)
      void navigate(`/runs/${run.id}`)
    },
    onError: notify.error,
  })

  const items = depths.map((value) => ({ value, label: t(`check.depth.${value}` as MessageKey) }))

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t('check.title')}</DialogTitle>
          <DialogDescription>{t('check.description')}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel>{t('check.depth')}</FieldLabel>
            <Select items={items} value={depth} onValueChange={(value) => setDepth(value as CheckDepth)}>
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {items.map((item) => (
                  <SelectItem key={item.value} value={item.value}>
                    {item.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          {depth === 'Subset' && (
            <Field>
              <FieldLabel htmlFor="check-percent">{t('check.percent')}</FieldLabel>
              <Input
                id="check-percent"
                type="number"
                min={1}
                max={100}
                value={percent}
                onChange={(event) => setPercent(event.target.value)}
              />
            </Field>
          )}
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t('common.cancel')}
          </Button>
          <Button disabled={plan.isPending} onClick={() => plan.mutate()}>
            {t('check.plan')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
