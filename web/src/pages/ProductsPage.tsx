import { useState } from 'react'
import { api, errorMessage } from '../api'
import { Empty, ErrorBox, Spinner } from '../components/common'
import { useBasket, useToast } from '../context/contexts'
import { formatPrice, initials } from '../format'
import { useAsync } from '../hooks/useAsync'
import type { Product } from '../types'

export function ProductsPage() {
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('')
  const [addingId, setAddingId] = useState<number | null>(null)
  const { basket, setBasket } = useBasket()
  const toast = useToast()

  const { data: categories } = useAsync(() => api.categories(), [])
  const { data, error, loading, reload } = useAsync(
    () => api.products({ search: search.trim() || undefined, category: category || undefined, pageSize: 50 }),
    [search, category],
  )

  async function handleAdd(product: Product) {
    setAddingId(product.id)
    try {
      const existing = basket?.items.find((i) => i.productId === product.id)?.quantity ?? 0
      const updated = await api.setBasketItem({
        productId: product.id,
        name: product.name,
        price: product.price,
        quantity: existing + 1,
      })
      setBasket(updated)
      toast.success(`"${product.name}" sepete eklendi.`)
    } catch (e) {
      toast.error(errorMessage(e))
    } finally {
      setAddingId(null)
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Ürün Kataloğu</h1>
          <p className="muted">Redis Cache-Aside + SQL Server Filtered/Covering Index ile sunulan ürünler</p>
        </div>
        <div className="toolbar">
          <input
            type="search"
            className="input"
            placeholder="Ürün veya açıklama ara..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <select className="input" value={category} onChange={(e) => setCategory(e.target.value)}>
            <option value="">Tüm Kategoriler</option>
            {(categories ?? []).map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        </div>
      </div>

      {loading && <Spinner label="Ürünler yükleniyor..." />}
      {error !== undefined && <ErrorBox error={error} onRetry={reload} />}

      {!loading && !error && (data?.items.length ?? 0) === 0 && (
        <Empty>Aramanızla eşleşen ürün bulunamadı.</Empty>
      )}

      <div className="product-grid">
        {(data?.items ?? []).map((p) => {
          const outOfStock = p.stock <= 0
          return (
            <div key={p.id} className="card product-card">
              <div className="product-thumb">
                {p.imageUrl ? <img src={p.imageUrl} alt={p.name} /> : <span>{initials(p.name)}</span>}
                <span className="product-cat">{p.category}</span>
              </div>
              <div className="product-body">
                <h3>{p.name}</h3>
                <p className="muted small product-desc">{p.description}</p>
                <div className="product-footer">
                  <div>
                    <div className="price">{formatPrice(p.price)}</div>
                    <div className={`small ${outOfStock ? 'text-danger' : 'muted'}`}>
                      {outOfStock ? 'Tükendi (Stok: 0)' : `Stok: ${p.stock} adet`}
                    </div>
                  </div>
                  <button
                    className="btn btn-primary"
                    disabled={addingId === p.id}
                    onClick={() => void handleAdd(p)}
                    title={outOfStock ? 'Stok yetersizliği Saga senaryosunu denemek için sepete ekleyebilirsiniz' : ''}
                  >
                    {addingId === p.id ? 'Ekleniyor...' : outOfStock ? 'Sepete Ekle (0 Stok Testi)' : 'Sepete Ekle'}
                  </button>
                </div>
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

