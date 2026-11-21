import { CheckIcon, LanguagesIcon, MonitorIcon, MoonIcon, SunIcon } from 'lucide-react'
import { GithubIcon } from '@/components/github-icon'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { useTheme, type Theme } from '@/components/theme-provider'
import { useI18n, type Locale, type MessageKey } from '@/lib/i18n'

const repository = 'https://github.com/imrcpan/my-backup'

const themes: { value: Theme; label: MessageKey; icon: typeof SunIcon }[] = [
  { value: 'light', label: 'theme.light', icon: SunIcon },
  { value: 'dark', label: 'theme.dark', icon: MoonIcon },
  { value: 'system', label: 'theme.system', icon: MonitorIcon },
]

const languages: { value: Locale; label: string }[] = [
  { value: 'zh', label: '中文' },
  { value: 'en', label: 'English' },
]

/** The theme and the language, in the header of every page. */
export function Preferences() {
  const { theme, setTheme } = useTheme()
  const { locale, setLocale, t } = useI18n()
  const current = themes.find((item) => item.value === theme) ?? themes[1]

  return (
    <div className="flex items-center gap-1">
      <Button
        variant="ghost"
        size="icon"
        aria-label={t('app.github')}
        title={t('app.github')}
        render={<a href={repository} target="_blank" rel="noopener noreferrer" />}
      >
        <GithubIcon />
      </Button>
      <DropdownMenu>
        <DropdownMenuTrigger render={<Button variant="ghost" size="icon" aria-label={t('language.toggle')} />}>
          <LanguagesIcon />
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-auto">
          {languages.map((item) => (
            <DropdownMenuItem key={item.value} onClick={() => setLocale(item.value)}>
              {item.label}
              {locale === item.value && <CheckIcon className="ml-auto" />}
            </DropdownMenuItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>
      <DropdownMenu>
        <DropdownMenuTrigger render={<Button variant="ghost" size="icon" aria-label={t('theme.toggle')} />}>
          <current.icon />
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-auto">
          {themes.map((item) => (
            <DropdownMenuItem key={item.value} onClick={() => setTheme(item.value)}>
              <item.icon />
              {t(item.label)}
              {theme === item.value && <CheckIcon className="ml-auto" />}
            </DropdownMenuItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  )
}
