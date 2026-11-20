import { toast } from 'sonner'
import { ApiError } from '@/lib/api'
import { useI18n, type MessageKey } from '@/lib/i18n'

/** Messages to the user: errors are shown in their language, by the code that the server gave. */
export function useNotify() {
  const { t, has } = useI18n()

  /** The text for an error: the translation of its code, or what the server said. */
  const describe = (error: unknown): string => {
    if (error instanceof ApiError) {
      const key = `error.${error.code}`
      return has(key) ? t(key) : error.message
    }
    return error instanceof Error ? error.message : t('error.unexpected')
  }

  return {
    describe,
    error: (error: unknown) => toast.error(describe(error)),
    success: (key: MessageKey) => toast.success(t(key)),
  }
}
