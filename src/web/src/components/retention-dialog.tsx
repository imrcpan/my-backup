import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
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
import { api } from '@/lib/api'
import { useI18n, type MessageKey } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'
import type { RetentionPolicy, StorageObject } from '@/lib/types'

const fields: { key: keyof RetentionPolicy; label: MessageKey }[] = [
  { key: 'keepLast', label: 'retention.last' },
  { key: 'keepDaily', label: 'retention.daily' },
  { key: 'keepWeekly', label: 'retention.weekly' },
  { key: 'keepMonthly', label: 'retention.monthly' },
  { key: 'keepYearly', label: 'retention.yearly' },
]

/** Sets how many snapshots of a repository are kept. Saving a policy removes nothing. */
export function RetentionDialog({
  open,
  onOpenChange,
  groupId,
  object,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  groupId: string
  object: StorageObject | undefined
}) {
  const { t } = useI18n()
  const notify = useNotify()
  const queryClient = useQueryClient()
  const [values, setValues] = useState<Record<string, string>>({})

  useEffect(() => {
    if (!open) return
    setValues(Object.fromEntries(fields.map((field) => [field.key, String(object?.retention?.[field.key] ?? 0)])))
  }, [open, object])

  const save = useMutation({
    mutationFn: (policy: RetentionPolicy | null) => api.groups.setRetention(groupId, object!.id, policy),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['groups'] })
      notify.success('retention.saved')
      onOpenChange(false)
    },
    onError: notify.error,
  })

  const policy = (): RetentionPolicy =>
    Object.fromEntries(fields.map((field) => [field.key, Math.max(0, Math.trunc(Number(values[field.key]) || 0))]))

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t('retention.title')}</DialogTitle>
          <DialogDescription>{t('retention.description')}</DialogDescription>
        </DialogHeader>
        <form
          id="retention-form"
          onSubmit={(event) => {
            event.preventDefault()
            save.mutate(policy())
          }}
        >
          <FieldGroup className="grid grid-cols-2 gap-4">
            {fields.map((field) => (
              <Field key={field.key}>
                <FieldLabel htmlFor={`retention-${field.key}`}>{t(field.label)}</FieldLabel>
                <Input
                  id={`retention-${field.key}`}
                  type="number"
                  min={0}
                  value={values[field.key] ?? '0'}
                  onChange={(event) => setValues({ ...values, [field.key]: event.target.value })}
                />
              </Field>
            ))}
          </FieldGroup>
        </form>
        <DialogFooter>
          {object?.retention && (
            <Button
              variant="outline"
              className="sm:mr-auto"
              disabled={save.isPending}
              onClick={() => save.mutate(null)}
            >
              {t('retention.remove')}
            </Button>
          )}
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" form="retention-form" disabled={save.isPending}>
            {t('common.save')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
