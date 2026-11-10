import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, errorMessage } from '../api'
import { ErrorBox } from '../components/common'
import { useAuth, useToast } from '../context/contexts'
import { formatDate, formatDateTime, formatPrice } from '../format'
import { useAsync } from '../hooks/useAsync'
import type { ChaosSettings, Product, TlvEntry, UserProfile } from '../types'

const SAMPLE_TLV = '9F02060000000150005A0841111111111111119F1A020792'

export function AdminPage() {
  const toast = useToast()
  const { user, isAdmin } = useAuth()

  // Üye yönetimi (ETicaret_IdentityDb)
  const {
    data: users,
    error: usersErr,
    reload: reloadUsers,
  } = useAsync(() => api.users(), [user?.id, isAdmin], undefined, isAdmin)

  // Ürün yönetimi
  const { data: productPage, error: prodErr, reload: reloadProducts } = useAsync(() => api.products({ pageSize: 100 }), [])
  const [editing, setEditing] = useState<Product | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [category, setCategory] = useState('Elektronik')
  const [price, setPrice] = useState(500)
  const [stock, setStock] = useState(10)

  // Chaos (Hata simülasyonu)
  const { data: catChaos } = useAsync(() => api.getCatalogChaos(), [])
  const { data: payChaos } = useAsync(() => api.getIssuerChaos(), [])
  const [catState, setCatState] = useState<ChaosSettings>({ delayMs: 0, failurePercent: 0 })
  const [payState, setPayState] = useState<ChaosSettings>({ delayMs: 0, failurePercent: 0 })

  useEffect(() => {
    if (catChaos) setCatState(catChaos)
  }, [catChaos])

  useEffect(() => {
    if (payChaos) setPayState(payChaos)
  }, [payChaos])

  // T-SQL Rapor
  const { data: report } = useAsync(() => api.dailyReport(30), [])

  // EMV TLV
  const [tlvHex, setTlvHex] = useState(SAMPLE_TLV)
  const [tlvRows, setTlvRows] = useState<TlvEntry[]>([])

  async function toggleUserRole(u: UserProfile) {
    try {
      const nextRole = u.role === 'Admin' ? 'Customer' : 'Admin'
      await api.updateUserRole(u.id, nextRole, u.isActive)
      toast.success(`${u.email} yetkisi '${nextRole}' olarak güncellendi.`)
      await reloadUsers()
    } catch (err) {
      toast.error(errorMessage(err))
    }
  }

  function startEdit(p: Product) {
    setEditing(p)
    setName(p.name)
    setDescription(p.description)
    setCategory(p.category)
    setPrice(p.price)
    setStock(p.stock)
  }

  function resetForm() {
    setEditing(null)
    setName('')
    setDescription('')
    setCategory('Elektronik')
    setPrice(500)
    setStock(10)
  }

  async function saveProduct(e: React.FormEvent) {
    e.preventDefault()
    try {
      if (editing) {
        await api.updateProduct(editing.id, { name, description, category, price, stock })
        toast.success('Ürün güncellendi (Redis önbelleği temizlendi).')
      } else {
        await api.createProduct({ name, description, category, price, stock })
        toast.success('Yeni ürün eklendi.')
      }
      resetForm()
      await reloadProducts()
    } catch (err) {
      toast.error(errorMessage(err))
    }
  }

  async function removeProduct(id: number) {
    try {
      await api.deleteProduct(id)
      toast.show('Ürün pasife alındı.', 'info')
      await reloadProducts()
    } catch (err) {
      toast.error(errorMessage(err))
    }
  }

  async function saveCatalogChaos() {
    try {
      await api.setCatalogChaos(catState)
      toast.success('Katalog hata simülasyonu güncellendi.')
    } catch (err) {
      toast.error(errorMessage(err))
    }
  }

  async function saveIssuerChaos() {
    try {
      await api.setIssuerChaos(payState)
      toast.success('Banka (Issuer) hata simülasyonu güncellendi.')
    } catch (err) {
      toast.error(errorMessage(err))
    }
  }

  async function handleDecodeTlv() {
    try {
      const rows = await api.decodeTlv(tlvHex)
      setTlvRows(rows)
    } catch (err) {
      toast.error(errorMessage(err))
    }
  }

  if (!isAdmin) {
    return (
      <div className="card" style={{ padding: 24 }}>
        <h2>Yönetici (Admin) Yetkisi Gerekli</h2>
        <p className="muted" style={{ margin: '10px 0 16px' }}>
          Bu ekran ve arka uçtaki yönetim API uçları (<code>RequireAuthorization(&quot;AdminOnly&quot;)</code>) yalnızca{' '}
          <strong>Admin</strong> rolüne sahip üyeler tarafından kullanılabilir.
        </p>
        <p className="small muted" style={{ marginBottom: 14 }}>
          Hazır Admin hesabı: <code>admin@minimagaza.com</code> / <code>Admin123!</code> veya Google ile Admin girişi.
        </p>
        <Link to="/giris" className="btn btn-primary">
          Admin Hesabıyla Giriş Yap
        </Link>
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
      <div className="page-header">
        <div>
          <h1>Yönetim Paneli (Admin)</h1>
          <p className="muted">
            Üye Veritabanı (<code>ETicaret_IdentityDb</code>), Kaos Mühendisliği (Polly & Saga), T-SQL Satış Raporu ve EMV TLV Çözücü
          </p>
        </div>
        <span className="badge badge-blue">Aktif Yönetici: {user?.email}</span>
      </div>

      {/* 0. Üye ve Rol Yönetimi (ETicaret_IdentityDb) */}
      <div className="card">
        <h3>Üye Veritabanı ve Yetki Yönetimi (ETicaret_IdentityDb → dbo.Users)</h3>
        <p className="muted small" style={{ marginBottom: 12 }}>
          Yerel (PBKDF2-SHA256) ve Google OAuth 2.0 ile kayıt olan tüm üyeler <code>ETicaret_IdentityDb</code> veritabanındaki{' '}
          <code>dbo.Users</code> tablosunda tutulur.
        </p>
        {usersErr !== undefined && <ErrorBox error={usersErr} onRetry={reloadUsers} />}
        <table className="table">
          <thead>
            <tr>
              <th>Ad Soyad</th>
              <th>E-posta</th>
              <th>Doğrulama Sağlayıcısı</th>
              <th>Rol</th>
              <th>Son Giriş</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {(users ?? []).map((u) => (
              <tr key={u.id}>
                <td>
                  <strong>{u.fullName}</strong>
                </td>
                <td className="mono">{u.email}</td>
                <td>
                  <span className="badge badge-gray">{u.provider}</span>
                </td>
                <td>
                  <span className={`badge ${u.role === 'Admin' ? 'badge-blue' : 'badge-green'}`}>{u.role}</span>
                </td>
                <td className="small muted">{u.lastLoginAt ? formatDateTime(u.lastLoginAt) : '—'}</td>
                <td style={{ textAlign: 'right' }}>
                  <button className="btn btn-sm" onClick={() => void toggleUserRole(u)}>
                    {u.role === 'Admin' ? 'Customer Yap' : 'Admin Yap'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* 1. Hata Simülasyonu */}
      <div className="card">
        <h3>Dağıtık Hata Simülasyonu (Polly Retry / Circuit Breaker & Saga Telafisi)</h3>
        <p className="muted small" style={{ marginBottom: 14 }}>
          Katalog servisinde gecikme/hata açtığınızda <code>Order.Api</code> içindeki Polly{' '}
          <code>StandardResilienceHandler</code> isteği otomatik olarak yeniden dener. Banka (Issuer) tarafında hata açtığınızda ise ödeme
          DE-39=91 ile düşer ve Saga ayrılan stoğu geri bırakır.
        </p>

        <div className="grid-2">
          <div className="subcard">
            <h4>Katalog Servisi (/api/products/chaos)</h4>
            <div className="field">
              <label>Yapay Gecikme (ms)</label>
              <input
                type="number"
                className="input"
                value={catState.delayMs}
                onChange={(e) => setCatState({ ...catState, delayMs: Number(e.target.value) })}
              />
            </div>
            <div className="field">
              <label>Hata Oranı (%{catState.failurePercent} → HTTP 503)</label>
              <input
                type="range"
                min={0}
                max={100}
                value={catState.failurePercent}
                onChange={(e) => setCatState({ ...catState, failurePercent: Number(e.target.value) })}
              />
            </div>
            <button className="btn btn-primary btn-sm" onClick={() => void saveCatalogChaos()}>
              Katalog Ayarını Uygula
            </button>
          </div>

          <div className="subcard">
            <h4>Banka / Issuer Switch (/api/payments/chaos)</h4>
            <div className="field">
              <label>Yapay Gecikme (ms)</label>
              <input
                type="number"
                className="input"
                value={payState.delayMs}
                onChange={(e) => setPayState({ ...payState, delayMs: Number(e.target.value) })}
              />
            </div>
            <div className="field">
              <label>Hata Oranı (%{payState.failurePercent} → RC=91 Switch Hatası)</label>
              <input
                type="range"
                min={0}
                max={100}
                value={payState.failurePercent}
                onChange={(e) => setPayState({ ...payState, failurePercent: Number(e.target.value) })}
              />
            </div>
            <button className="btn btn-primary btn-sm" onClick={() => void saveIssuerChaos()}>
              Banka Ayarını Uygula
            </button>
          </div>
        </div>
      </div>

      {/* 2. T-SQL Günlük Satış Raporu */}
      <div className="card">
        <h3>Günlük Satış Raporu (Dapper + T-SQL Stored Procedure)</h3>
        <p className="muted small" style={{ marginBottom: 12 }}>
          <code>dbo.sp_GunlukSatisRaporu</code> saklı yordamı CTE ve Window Function (<code>SUM() OVER</code>,{' '}
          <code>DENSE_RANK() OVER</code>) kullanarak kümülatif ciroyu ve ciro sıralamasını hesaplar.
        </p>
        <table className="table">
          <thead>
            <tr>
              <th>Tarih</th>
              <th>Onaylı Sipariş Sayısı</th>
              <th>Günlük Ciro</th>
              <th>Kümülatif Ciro (SUM OVER)</th>
              <th>Ciro Sırası (DENSE_RANK)</th>
            </tr>
          </thead>
          <tbody>
            {(report ?? []).map((r) => (
              <tr key={r.day}>
                <td>{formatDate(r.day)}</td>
                <td>{r.orderCount}</td>
                <td>{formatPrice(r.totalAmount)}</td>
                <td>{formatPrice(r.runningTotal)}</td>
                <td>#{r.rankByAmount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* 3. EMV BER-TLV Çözücü */}
      <div className="card">
        <h3>EMV BER-TLV (DE-55 Çip Verisi) Çözücü</h3>
        <p className="muted small" style={{ marginBottom: 10 }}>
          EMV çipli işlemlerde kullanılan Tag-Length-Value (ör. <code>9F02</code> Tutar, <code>5A</code> PAN,{' '}
          <code>9F1A</code> Ülke Kodu) hex verisini ayrıştırır.
        </p>
        <div style={{ display: 'flex', gap: 8 }}>
          <input className="input mono" value={tlvHex} onChange={(e) => setTlvHex(e.target.value)} />
          <button className="btn btn-primary" onClick={() => void handleDecodeTlv()}>
            TLV Çözümle
          </button>
        </div>
        {tlvRows.length > 0 && (
          <table className="table" style={{ marginTop: 12 }}>
            <thead>
              <tr>
                <th>Tag</th>
                <th>EMV Alan Adı</th>
                <th>Uzunluk (Bayt)</th>
                <th>Değer (Hex)</th>
              </tr>
            </thead>
            <tbody>
              {tlvRows.map((t, i) => (
                <tr key={i}>
                  <td>
                    <code>{t.tag}</code>
                  </td>
                  <td>{t.name}</td>
                  <td>{t.length}</td>
                  <td className="mono">{t.value}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* 4. Ürün Yönetimi */}
      <div className="card">
        <h3>Ürün Yönetimi</h3>
        {prodErr !== undefined && <ErrorBox error={prodErr} onRetry={reloadProducts} />}
        <form className="row-form" onSubmit={(e) => void saveProduct(e)}>
          <input className="input" placeholder="Ürün adı" value={name} onChange={(e) => setName(e.target.value)} required />
          <input
            className="input"
            placeholder="Açıklama"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
          <input
            className="input"
            placeholder="Kategori"
            value={category}
            onChange={(e) => setCategory(e.target.value)}
            required
          />
          <input
            type="number"
            className="input"
            placeholder="Fiyat"
            value={price}
            onChange={(e) => setPrice(Number(e.target.value))}
            required
          />
          <input
            type="number"
            className="input"
            placeholder="Stok"
            value={stock}
            onChange={(e) => setStock(Number(e.target.value))}
            required
          />
          <button type="submit" className="btn btn-primary">
            {editing ? 'Güncelle' : 'Ekle'}
          </button>
          {editing && (
            <button type="button" className="btn" onClick={resetForm}>
              Vazgeç
            </button>
          )}
        </form>

        <table className="table" style={{ marginTop: 14 }}>
          <thead>
            <tr>
              <th>#</th>
              <th>Ürün</th>
              <th>Kategori</th>
              <th>Fiyat</th>
              <th>Stok</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {(productPage?.items ?? []).map((p) => (
              <tr key={p.id}>
                <td>{p.id}</td>
                <td>{p.name}</td>
                <td>{p.category}</td>
                <td>{formatPrice(p.price)}</td>
                <td>{p.stock}</td>
                <td style={{ textAlign: 'right' }}>
                  <button className="btn btn-sm" onClick={() => startEdit(p)} style={{ marginRight: 6 }}>
                    Düzenle
                  </button>
                  <button className="btn btn-sm btn-danger" onClick={() => void removeProduct(p.id)}>
                    Sil
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
