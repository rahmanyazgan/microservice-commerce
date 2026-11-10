import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { AuthProvider } from './context/AuthProvider'
import { BasketProvider } from './context/BasketProvider'
import { ToastProvider } from './context/ToastProvider'
import { AdminPage } from './pages/AdminPage'
import { ApiDocsPage } from './pages/ApiDocsPage'
import { BasketPage } from './pages/BasketPage'
import { LoginPage } from './pages/LoginPage'
import { NotificationsPage } from './pages/NotificationsPage'
import { OrdersPage } from './pages/OrdersPage'
import { PaymentDetailPage } from './pages/PaymentDetailPage'
import { PaymentPage } from './pages/PaymentPage'
import { ProductsPage } from './pages/ProductsPage'

export default function App() {
  return (
    <BrowserRouter>
      <ToastProvider>
        <AuthProvider>
          <BasketProvider>
            <Routes>
              <Route element={<Layout />}>
                <Route index element={<ProductsPage />} />
                <Route path="/giris" element={<LoginPage />} />
                <Route path="/sepet" element={<BasketPage />} />
                <Route path="/odeme/:orderId" element={<PaymentPage />} />
                <Route path="/siparisler" element={<OrdersPage />} />
                <Route path="/odeme-detay/:paymentId" element={<PaymentDetailPage />} />
                <Route path="/bildirimler" element={<NotificationsPage />} />
                <Route path="/yonetim" element={<AdminPage />} />
                <Route path="/api-dokumani" element={<ApiDocsPage />} />
              </Route>
            </Routes>
          </BasketProvider>
        </AuthProvider>
      </ToastProvider>
    </BrowserRouter>
  )
}
