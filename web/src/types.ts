export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  traceId?: string
  correlationId?: string
  errors?: Record<string, string[]>
}

export interface ServiceHealth {
  name: string
  status: 'Healthy' | 'Unhealthy' | string
  durationMs: number
}

export interface HealthReport {
  status: 'Healthy' | 'Degraded' | string
  services: ServiceHealth[]
}

export interface Product {
  id: number
  name: string
  description: string
  category: string
  price: number
  stock: number
  imageUrl: string | null
  isActive: boolean
}

export interface ProductInput {
  name: string
  description: string
  category: string
  price: number
  stock: number
  imageUrl?: string | null
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface ChaosSettings {
  delayMs: number
  failurePercent: number
}

export interface BasketItem {
  productId: number
  name: string
  price: number
  quantity: number
  lineTotal: number
}

export interface Basket {
  customerId: string
  items: BasketItem[]
  total: number
}

export type OrderStatus = 'Pending' | 'StockReserved' | 'Confirmed' | 'Cancelled'

export interface OrderItem {
  productId: number
  productName: string
  unitPrice: number
  quantity: number
}

export interface OrderHistoryEntry {
  status: OrderStatus | string
  note: string
  occurredAt: string
}

export interface Order {
  id: string
  customerId: string
  status: OrderStatus
  statusText: string
  totalAmount: number
  cancelReason: string | null
  paymentId: string | null
  createdAt: string
  items: OrderItem[]
  history: OrderHistoryEntry[]
}

export interface DailySalesRow {
  day: string
  orderCount: number
  totalAmount: number
  runningTotal: number
  rankByAmount: number
}

export interface TestCard {
  number: string
  brand: string
  description: string
  threeDs: boolean
  expectedResult: string
}

export type PaymentStatus =
  | 'ThreeDsPending'
  | 'Approved'
  | 'Declined'
  | 'Failed'
  | 'Cancelled'
  | 'Reversed'
  | 'AwaitingCard'

export interface ThreeDsChallenge {
  challengeId: string
  maskedPan: string
  amount: number
  merchantName: string
  remainingAttempts: number
}

export interface PaymentResult {
  paymentId: string
  orderId: string
  status: PaymentStatus
  message: string
  responseCode: string | null
  threeDs: ThreeDsChallenge | null
}

export interface PaymentRequest {
  orderId: string
  cardHolder: string
  cardNumber: string
  expiryMonth: number
  expiryYear: number
  cvv: string
}

export interface IsoField {
  number: number
  name: string
  value: string
}

export interface IsoView {
  mti: string
  mtiText: string
  bitmap: string
  raw: string
  hex: string
  fields: IsoField[]
}

export interface PaymentDetail {
  id: string
  orderId: string
  amount: number
  currency: string
  maskedPan: string
  brand: string
  status: PaymentStatus | string
  responseCode: string | null
  responseText: string | null
  authCode: string | null
  rrn: string | null
  stan: string | null
  eci: string | null
  createdAt: string
  isoRequest: IsoView | null
  isoResponse: IsoView | null
  reversalRequest: IsoView | null
}

export interface TlvEntry {
  tag: string
  name: string
  length: number
  value: string
}

export interface NotificationItem {
  id: string
  customerId: string
  type: 'OrderConfirmed' | 'OrderCancelled' | string
  title: string
  message: string
  createdAt: string
}

export interface UserProfile {
  id: string
  email: string
  fullName: string
  role: 'Admin' | 'Customer' | string
  provider: 'Local' | 'Google' | string
  avatarUrl?: string | null
  isActive: boolean
  createdAt: string
  lastLoginAt?: string | null
}

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  user: UserProfile
}

export interface AuthConfig {
  googleClientId: string
  databaseName: string
  demoAccounts: { email: string; password: string; role: string; label: string }[]
}
