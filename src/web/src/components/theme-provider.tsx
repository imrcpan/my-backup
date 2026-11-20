import { createContext, useContext, useEffect, useState } from 'react'

export type Theme = 'dark' | 'light' | 'system'

const storageKey = 'backup-theme'

type ThemeContextValue = { theme: Theme; setTheme: (theme: Theme) => void }

const ThemeContext = createContext<ThemeContextValue | undefined>(undefined)

function readTheme(): Theme {
  try {
    const saved = localStorage.getItem(storageKey)
    if (saved === 'dark' || saved === 'light' || saved === 'system') return saved
  } catch {
    // Storage can be blocked; the default is used.
  }
  return 'dark'
}

function apply(theme: Theme) {
  const dark = theme === 'dark' || (theme === 'system' && matchMedia('(prefers-color-scheme: dark)').matches)
  const root = document.documentElement
  root.classList.toggle('dark', dark)
  root.style.colorScheme = dark ? 'dark' : 'light'
  root.style.backgroundColor = ''
}

/** Dark is the default. The page already has its theme from the script in index.html when this runs. */
export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(readTheme)

  useEffect(() => {
    apply(theme)
    if (theme !== 'system') return
    const query = matchMedia('(prefers-color-scheme: dark)')
    const onChange = () => apply('system')
    query.addEventListener('change', onChange)
    return () => query.removeEventListener('change', onChange)
  }, [theme])

  const setTheme = (next: Theme) => {
    try {
      localStorage.setItem(storageKey, next)
    } catch {
      // Not saved; it still applies for this visit.
    }
    setThemeState(next)
  }

  return <ThemeContext.Provider value={{ theme, setTheme }}>{children}</ThemeContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useTheme() {
  const context = useContext(ThemeContext)
  if (!context) throw new Error('useTheme must be used inside ThemeProvider')
  return context
}
