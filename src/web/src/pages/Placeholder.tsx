import { Page } from '@/components/page'
import { useI18n, type MessageKey } from '@/lib/i18n'

export function Placeholder({ title, description }: { title: MessageKey; description: MessageKey }) {
  const { t } = useI18n()
  return (
    <Page title={t(title)} description={t(description)}>
      <p className="text-sm text-muted-foreground">{t('common.coming')}</p>
    </Page>
  )
}
