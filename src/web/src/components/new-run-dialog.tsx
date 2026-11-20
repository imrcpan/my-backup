import { useEffect, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { LockIcon, LockOpenIcon } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldDescription, FieldGroup, FieldLabel, FieldLegend, FieldSet } from '@/components/ui/field'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { api } from '@/lib/api'
import { useI18n } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'
import type { Device, Group, StorageObject } from '@/lib/types'

/**
 * Starts a run: one object is the newest copy, and the others (all of them by default) are brought
 * up to date from it. This only asks for a plan; the plan is shown on its own page to be confirmed.
 */
export function NewRunDialog({
  open,
  onOpenChange,
  group,
  devices,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  group: Group | undefined
  devices: Device[]
}) {
  const { t } = useI18n()
  const notify = useNotify()
  const navigate = useNavigate()
  const [source, setSource] = useState('')
  const [targets, setTargets] = useState<string[]>([])

  useEffect(() => {
    if (!open || !group) return
    const first = group.objects[0]?.id ?? ''
    setSource(first)
    setTargets(group.objects.filter((item) => item.id !== first).map((item) => item.id))
  }, [open, group])

  const choose = (id: string) => {
    setSource(id)
    // The newest copy cannot be its own target; the rest stay chosen.
    setTargets(group?.objects.filter((item) => item.id !== id).map((item) => item.id) ?? [])
  }

  const plan = useMutation({
    mutationFn: () => api.runs.planSync(group!.id, source, targets),
    onSuccess: (run) => {
      onOpenChange(false)
      void navigate(`/runs/${run.id}`)
    },
    onError: notify.error,
  })

  const label = (item: StorageObject) => {
    const device = devices.find((candidate) => candidate.id === item.deviceId)
    return (
      <span className="flex min-w-0 flex-col">
        <span className="flex items-center gap-2 text-sm font-medium">
          {device?.name ?? item.deviceId}
          {item.encrypted ? (
            <LockIcon className="size-3.5 text-muted-foreground" />
          ) : (
            <LockOpenIcon className="size-3.5 text-muted-foreground" />
          )}
        </span>
        <span className="font-mono text-xs break-all text-muted-foreground">{item.path}</span>
      </span>
    )
  }

  const others = group?.objects.filter((item) => item.id !== source) ?? []
  const allChosen = others.length > 0 && others.every((item) => targets.includes(item.id))

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t('newrun.title')}</DialogTitle>
          <DialogDescription>{t('newrun.description')}</DialogDescription>
        </DialogHeader>
        <FieldGroup className="max-h-[60vh] overflow-y-auto">
          <FieldSet>
            <FieldLegend variant="label">{t('newrun.source')}</FieldLegend>
            <FieldDescription>{t('newrun.source.hint')}</FieldDescription>
            <RadioGroup value={source} onValueChange={choose} className="gap-2">
              {group?.objects.map((item) => (
                <Field key={item.id} orientation="horizontal" className="items-center">
                  <RadioGroupItem value={item.id} id={`source-${item.id}`} />
                  <FieldLabel htmlFor={`source-${item.id}`} className="flex-1 cursor-pointer font-normal">
                    {label(item)}
                  </FieldLabel>
                </Field>
              ))}
            </RadioGroup>
          </FieldSet>
          <FieldSet>
            <div className="flex items-center justify-between">
              <FieldLegend variant="label" className="mb-0">
                {t('newrun.targets')}
              </FieldLegend>
              <Button
                type="button"
                variant="ghost"
                size="xs"
                onClick={() => setTargets(allChosen ? [] : others.map((item) => item.id))}
              >
                {t('newrun.targets.all')}
              </Button>
            </div>
            <FieldDescription>{t('newrun.targets.hint')}</FieldDescription>
            <div className="flex flex-col gap-2">
              {others.map((item) => (
                <Field key={item.id} orientation="horizontal" className="items-center">
                  <Checkbox
                    id={`target-${item.id}`}
                    checked={targets.includes(item.id)}
                    onCheckedChange={(checked) =>
                      setTargets(checked ? [...targets, item.id] : targets.filter((id) => id !== item.id))
                    }
                  />
                  <FieldLabel htmlFor={`target-${item.id}`} className="flex-1 cursor-pointer font-normal">
                    {label(item)}
                  </FieldLabel>
                </Field>
              ))}
            </div>
          </FieldSet>
        </FieldGroup>
        <p className="text-xs text-muted-foreground">{t('newrun.nothing')}</p>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t('common.cancel')}
          </Button>
          <Button disabled={plan.isPending || source === '' || targets.length === 0} onClick={() => plan.mutate()}>
            {plan.isPending ? t('newrun.planning') : t('newrun.plan')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
