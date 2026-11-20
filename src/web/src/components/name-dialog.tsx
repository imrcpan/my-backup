import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useI18n } from '@/lib/i18n'

/** Asks for one name and nothing else. Used to make a group and to rename one. */
export function NameDialog({
  open,
  onOpenChange,
  title,
  description,
  initial = '',
  pending,
  onSubmit,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  initial?: string
  pending: boolean
  onSubmit: (name: string) => void
}) {
  const { t } = useI18n()
  const [name, setName] = useState(initial)

  useEffect(() => {
    if (open) setName(initial)
  }, [open, initial])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description && <DialogDescription>{description}</DialogDescription>}
        </DialogHeader>
        <form
          id="name-form"
          onSubmit={(event) => {
            event.preventDefault()
            onSubmit(name)
          }}
        >
          <Field>
            <FieldLabel htmlFor="group-name">{t('groups.field.name')}</FieldLabel>
            <Input
              id="group-name"
              value={name}
              autoFocus
              autoComplete="off"
              onChange={(event) => setName(event.target.value)}
            />
          </Field>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" form="name-form" disabled={pending}>
            {t('common.save')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
