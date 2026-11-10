import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, errorMessage } from '../api'
import { Empty, ErrorBox, Spinner } from '../components/common'
import { useBasket, useToast } from '../context/contexts'
import { formatPrice } from '../format'
import type { BasketItem } from '../types'

export function BasketPage() {
  const { basket, loading, error, refresh, setBasket } = useBasket()
  const [submitting, setSubmitting] = useState(false)
  const toast = useToast()
  const navigate = useNavigate()

  async function updateQty(item: BasketItem, nextQty: number) {
    try {
      const updated = await api.setBasketItem({
        productId: item.productId,
        name: item.name,
        price: item.price,
        quantity: nextQty,
      })
      setBasket(updated)
    } catch (e) {
      toast.error(errorMessage(e))
    }
  }

  async function handleCheckout() {
    if (!basket || basket.items.length === 0) return
    setSubmitting(true)
    try {
      const order = await api.createOrder(
        basket.items.map((i) => ({ productId: i.productId, quantity: i.quantity })),
      )
      await api.clearBasket()
      setBasket({ customerId: basket.customerId, items: [], total: 0 })
      toast.success('Sipariş oluşturuldu; stok rezervasyonu başlatıldı.')
      void navigate(`/odeme/${order.id}`)
    } catch (e) {
      toast.error(errorMessage(e))
    } finally {
      setSubmitting(false)
    }
  }

  if (loading) return <Spinner label="Sepet yükleniyor..." />
  if (error !== undefined) return <ErrorBox error={error} onRetry={refresh} />

  const items = basket?.items ?? []

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Sepetim</h1>
          <p className="muted">Redis üzerinde 24 saatlik TTL ile saklanan müşteri sepeti</p>
        </div>
      </div>

      {items.length === 0 ? (
        <Empty>
          <p>Sepetinizde henüz ürün bulunmuyor.</p>
          <Link to="/" className="btn btn-primary" style={{ marginTop: 12, display: 'inline-block' }}>
            Ürünlere Göz At
          </Link>
        </Empty>
      ) : (
        <div className="card">
          <table className="table">
            <thead>
              <tr>
                <th>Ürün</th>
                <th>Birim Fiyat</th>
                <th>Adet</th>
                <th>Ara Toplam</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.productId}>
                  <td>
                    <strong>{item.name}</strong>
                  </td>
                  <td>{formatPrice(item.price)}</td>
                  <td>
                    <div className="qty-control">
                      <button className="btn btn-sm" onClick={() => void updateQty(item, item.quantity - 1)}>
                        −
                      </button>
                      <span>{item.quantity}</span>
                      <button className="btn btn-sm" onClick={() => void updateQty(item, item.quantity + 1)}>
                        +
                      </button>
                    </div>
                  </td>
                  <td>
                    <strong>{formatPrice(item.lineTotal)}</strong>
                  </td>
                  <td style={{ textAlign: 'right' }}>
                    <button className="btn btn-sm btn-danger" onClick={() => void updateQty(item, 0)}>
                      Kaldır
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          <div className="basket-summary">
            <div>
              <span className="muted">Genel Toplam:</span>{' '}
              <strong className="price-lg">{formatPrice(basket?.total ?? 0)}</strong>
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <button
                className="btn"
                onClick={() => void api.clearBasket().then(() => setBasket({ customerId: 'demo-musteri', items: [], total: 0 }))}
              >
                Sepeti Temizle
              </button>
              <button className="btn btn-primary" disabled={submitting} onClick={() => void handleCheckout()}>
                {submitting ? 'Sipariş Oluşturuluyor...' : 'Siparişi Oluştur ve Ödemeye Geç'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

