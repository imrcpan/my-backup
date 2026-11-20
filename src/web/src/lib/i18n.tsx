import { createContext, useContext, useEffect, useState } from 'react'
import { en } from '@/locales/en'
import { zh } from '@/locales/zh'

export type Locale = 'en' | 'zh'
export type MessageKey = keyof typeof en

const storageKey = 'backup-language'
const dictionaries: Record<Locale, Record<MessageKey, string>> = { en, zh }

type I18nContextValue = {
  locale: Locale
  setLocale: (locale: Locale) => void
  /** The text for a key; {name} in the text is replaced by params.name. */
  t: (key: MessageKey, params?: Record<string, string | number>) => string
}

const I18nContext = createContext<I18nContextValue | undefined>(undefined)

function initialLocale(): Locale {
  try {
    const saved = localStorage.getItem(storageKey)
    if (saved === 'en' || saved === 'zh') return saved
  } catch {
    // Storage can be blocked; the language of the browser is used.
  }
  return navigator.language.startsWith('zh') ? 'zh' : 'en'
}

export function I18nProvider({ children }: { children: React.ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>(initialLocale)

  useEffect(() => {
    document.documentElement.lang = locale
  }, [locale])

  const setLocale = (next: Locale) => {
    try {
      localStorage.setItem(storageKey, next)
    } catch {
      // Not saved; it still applies for this visit.
    }
    setLocaleState(next)
  }

  const t: I18nContextValue['t'] = (key, params) =>
    dictionaries[locale][key].replace(/\{(\w+)\}/g, (whole, name: string) =>
      params && name in params ? String(params[name]) : whole,
    )

  return <I18nContext.Provider value={{ locale, setLocale, t }}>{children}</I18nContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useI18n() {
  const context = useContext(I18nContext)
  if (!context) throw new Error('useI18n must be used inside I18nProvider')
  return context
}
