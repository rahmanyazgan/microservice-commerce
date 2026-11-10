import type {
  AuthConfig,
  AuthResponse,
  Basket,
  ChaosSettings,
  DailySalesRow,
  HealthReport,
  NotificationItem,
  Order,
  PagedResult,
  PaymentDetail,
  PaymentRequest,
  PaymentResult,
  ProblemDetails,
  Product,
  ProductInput,
  TestCard,
  TlvEntry,
  UserProfile,
} from './types'

const TOKEN_STORAGE_KEY = 'minimagaza_token'
const USER_STORAGE_KEY = 'minimagaza_user'

export function getStoredToken(): string | null {
  return localStorage.getItem(TOKEN_STORAGE_KEY)
}

export function getStoredUser(): UserProfile | null {
  const raw = localStorage.getItem(USER_STORAGE_KEY)
  if (!raw) return null
  try {
    return JSON.parse(raw) as UserProfile
  } catch {
    return null
  }
}

export function saveAuthSession(auth: AuthResponse): void {
  localStorage.setItem(TOKEN_STORAGE_KEY, auth.accessToken)
  localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(auth.user))
}

export function clearAuthSession(): void {
  localStorage.removeItem(TOKEN_STORAGE_KEY)
  localStorage.removeItem(USER_STORAGE_KEY)
}

export function currentCustomerId(): string {
  return getStoredUser()?.email ?? 'demo-musteri'
}

export const CUSTOMER_ID = 'demo-musteri'

export class ApiError extends Error {
  status: number
  title: string
  detail?: string
  errors: Record<string, string[]>
  correlationId?: string

  constructor(status: number, problem: ProblemDetails) {
    const title = problem.title || defaultTitle(status)
    super(problem.detail ? `${title}: ${problem.detail}` : title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.detail = problem.detail
    this.errors = normalizeErrors(problem.errors)
    this.correlationId = problem.correlationId
  }

  /** Returns first error message for a field (case-insensitive key match). */
  fieldError(field: string): string | undefined {
    return this.errors[field.toLowerCase()]?.[0]
  }
}

function normalizeErrors(errors?: Record<string, string[]>): Record<string, string[]> {
  const result: Record<string, string[]> = {}
  if (!errors) return result
  for (const [key, value] of Object.entries(errors)) {
    // "$.cardNumber" / "CardNumber" / "request.CardNumber" -> "cardnumber"
    const clean = key.replace(/^\$\./, '').split('.').pop() ?? key
    result[clean.toLowerCase()] = Array.isArray(value) ? value : [String(value)]
  }
  return result
}

function defaultTitle(status: number): string {
  if (status === 0) return 'Sunucuya ulaşılamadı. Servisler çalışıyor mu?'
  if (status === 400) return 'Geçersiz istek'
  if (status === 404) return 'Kayıt bulunamadı'
  if (status === 409) return 'İşlem şu anda yapılamıyor'
  if (status >= 500) return 'Sunucu hatası oluştu'
  return `Beklenmeyen hata (${status})`
}

export function errorMessage(err: unknown): string {
  if (err instanceof ApiError) return err.message
  if (err instanceof Error) return err.message
  return 'Bilinmeyen bir hata oluştu'
}

interface RequestOptions {
  method?: string
  body?: unknown
  headers?: Record<string, string>
  signal?: AbortSignal
}

export async function request<T>(url: string, options: RequestOptions = {}): Promise<T> {
  const token = getStoredToken()
  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...options.headers,
  }
  if (options.body !== undefined) headers['Content-Type'] = 'application/json'

  let response: Response
  try {
    response = await fetch(url, {
      method: options.method ?? 'GET',
      headers,
      body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
      signal: options.signal,
    })
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') throw e
    throw new ApiError(0, {})
  }

  const text = await response.text()
  let data: unknown = undefined
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      data = undefined
    }
  }

  if (!response.ok) {
    const problem: ProblemDetails =
      data && typeof data === 'object' ? (data as ProblemDetails) : {}
    // Proxy errors (gateway down) come back as 500/502/504 with empty body
    if (!text && (response.status === 502 || response.status === 504 || response.status === 500)) {
      throw new ApiError(0, {})
    }
    throw new ApiError(response.status, problem)
  }

  return data as T
}

function qs(params: Record<string, string | number | undefined>): string {
  const sp = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== '') sp.set(k, String(v))
  }
  const s = sp.toString()
  return s ? `?${s}` : ''
}

export const api = {
  health: () => request<HealthReport>('/api/health'),

  // Catalog
  products: (p: { search?: string; category?: string; page?: number; pageSize?: number } = {}) =>
    request<PagedResult<Product>>(`/api/products${qs(p)}`),
  product: (id: number) => request<Product>(`/api/products/${id}`),
  categories: () => request<string[]>('/api/products/categories'),
  createProduct: (body: ProductInput) =>
    request<Product>('/api/products', { method: 'POST', body }),
  updateProduct: (id: number, body: ProductInput) =>
    request<Product>(`/api/products/${id}`, { method: 'PUT', body }),
  deleteProduct: (id: number) => request<void>(`/api/products/${id}`, { method: 'DELETE' }),
  getCatalogChaos: () => request<ChaosSettings>('/api/products/chaos'),
  setCatalogChaos: (body: ChaosSettings) =>
    request<ChaosSettings>('/api/products/chaos', { method: 'PUT', body }),

  // Basket
  basket: (customerId = currentCustomerId()) => request<Basket>(`/api/basket/${encodeURIComponent(customerId)}`),
  setBasketItem: (
    item: { productId: number; name: string; price: number; quantity: number },
    customerId = currentCustomerId(),
  ) => request<Basket>(`/api/basket/${encodeURIComponent(customerId)}/items`, { method: 'PUT', body: item }),
  removeBasketItem: (productId: number, customerId = currentCustomerId()) =>
    request<Basket>(`/api/basket/${encodeURIComponent(customerId)}/items/${productId}`, { method: 'DELETE' }),
  clearBasket: (customerId = currentCustomerId()) =>
    request<void>(`/api/basket/${encodeURIComponent(customerId)}`, { method: 'DELETE' }),

  // Orders
  createOrder: (items: { productId: number; quantity: number }[], customerId = currentCustomerId()) =>
    request<Order>('/api/orders', { method: 'POST', body: { customerId, items } }),
  orders: (customerId = currentCustomerId()) => request<Order[]>(`/api/orders${qs({ customerId })}`),
  order: (id: string) => request<Order>(`/api/orders/${id}`),
  dailyReport: (days = 30) => request<DailySalesRow[]>(`/api/orders/reports/daily${qs({ days })}`),

  // Payments
  testCards: () => request<TestCard[]>('/api/payments/test-cards'),
  pay: (body: PaymentRequest, idempotencyKey: string) =>
    request<PaymentResult>('/api/payments', {
      method: 'POST',
      body,
      headers: { 'Idempotency-Key': idempotencyKey },
    }),
  verify3ds: (paymentId: string, otp: string) =>
    request<PaymentResult>(`/api/payments/${paymentId}/3ds`, { method: 'POST', body: { otp } }),
  payment: (id: string) => request<PaymentDetail>(`/api/payments/${id}`),
  paymentByOrder: (orderId: string) => request<PaymentDetail>(`/api/payments/by-order/${orderId}`),
  decodeTlv: (hex: string) =>
    request<TlvEntry[]>('/api/payments/tools/emv-tlv', { method: 'POST', body: { hex } }),
  getIssuerChaos: () => request<ChaosSettings>('/api/payments/chaos'),
  setIssuerChaos: (body: ChaosSettings) =>
    request<ChaosSettings>('/api/payments/chaos', { method: 'PUT', body }),

  // Notifications
  notifications: (customerId = currentCustomerId()) =>
    request<NotificationItem[]>(`/api/notifications${qs({ customerId })}`),

  // Identity & Auth (ETicaret_IdentityDb)
  authConfig: () => request<AuthConfig>('/api/auth/config'),
  login: (email: string, password: string) =>
    request<AuthResponse>('/api/auth/login', { method: 'POST', body: { email, password } }),
  register: (email: string, fullName: string, password: string) =>
    request<AuthResponse>('/api/auth/register', { method: 'POST', body: { email, fullName, password } }),
  googleLogin: (body: { credential?: string; email?: string; fullName?: string; requestedRole?: string }) =>
    request<AuthResponse>('/api/auth/google', { method: 'POST', body }),
  me: () => request<UserProfile>('/api/auth/me'),
  users: () => request<UserProfile[]>('/api/auth/users'),
  updateUserRole: (id: string, role: 'Admin' | 'Customer', isActive?: boolean) =>
    request<UserProfile>(`/api/auth/users/${id}/role`, { method: 'PUT', body: { role, isActive } }),
}
