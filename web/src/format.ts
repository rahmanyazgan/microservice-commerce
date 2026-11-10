const currency = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })
const dateTime = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
const timeOnly = new Intl.DateTimeFormat('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
const dateOnly = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium' })

export const formatPrice = (value: number) => currency.format(value ?? 0)

export function formatDateTime(value: string): string {
  const d = new Date(value)
  return isNaN(d.getTime()) ? value : dateTime.format(d)
}

export function formatTime(value: string): string {
  const d = new Date(value)
  return isNaN(d.getTime()) ? value : timeOnly.format(d)
}

export function formatDate(value: string): string {
  const d = new Date(value.length === 10 ? `${value}T00:00:00` : value)
  return isNaN(d.getTime()) ? value : dateOnly.format(d)
}

export const shortId = (id: string) => id.slice(0, 8).toUpperCase()

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toLocaleUpperCase('tr-TR'))
    .join('')
}

// ---- Card helpers ----
export const digitsOnly = (s: string) => s.replace(/\D/g, '')

export function formatCardNumber(value: string): string {
  return digitsOnly(value)
    .slice(0, 19)
    .replace(/(.{4})/g, '$1 ')
    .trim()
}

export function detectBrand(value: string): 'Visa' | 'Mastercard' | 'Troy' | null {
  const d = digitsOnly(value)
  if (d.startsWith('9792')) return 'Troy'
  if (/^4/.test(d)) return 'Visa'
  if (/^(5[1-5]|2[2-7])/.test(d)) return 'Mastercard'
  return null
}

export function luhnValid(value: string): boolean {
  const d = digitsOnly(value)
  if (d.length < 12) return false
  let sum = 0
  let dbl = false
  for (let i = d.length - 1; i >= 0; i--) {
    let n = d.charCodeAt(i) - 48
    if (dbl) {
      n *= 2
      if (n > 9) n -= 9
    }
    sum += n
    dbl = !dbl
  }
  return sum % 10 === 0
}
