import { Link, NavLink, Outlet } from 'react-router-dom'
import { api } from '../api'
import { useAuth, useBasket } from '../context/contexts'
import { useAsync } from '../hooks/useAsync'

const SERVICES = ['Katalog', 'Sepet', 'Sipariş', 'Ödeme', 'Bildirim', 'Kimlik']

function HealthBar() {
  const { data, error } = useAsync(() => api.health(), [], 10000)
  const services = data?.services ?? SERVICES.map((name) => ({ name, status: 'Unknown', durationMs: 0 }))
  const gatewayDown = !!error && !data

  return (
    <div className="health-bar">
      <span className="health-title">Sistem durumu:</span>
      <span className="health-item" title="API Gateway">
        <span className={`dot ${error ? 'dot-red' : data ? 'dot-green' : 'dot-gray'}`} />
        Gateway
      </span>
      {services.map((s) => (
        <span key={s.name} className="health-item" title={s.durationMs ? `${s.durationMs} ms` : undefined}>
          <span
            className={`dot ${
              gatewayDown ? 'dot-red' : s.status === 'Healthy' ? 'dot-green' : s.status === 'Unknown' ? 'dot-gray' : 'dot-red'
            }`}
          />
          {s.name}
        </span>
      ))}
    </div>
  )
}

export function Layout() {
  const { count, refresh: refreshBasket } = useBasket()
  const { user, isAdmin, logout } = useAuth()
  const navClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'nav-link active' : 'nav-link')

  return (
    <div className="app">
      <header className="header">
        <div className="container header-inner">
          <NavLink to="/" className="logo">
            <span className="logo-mark">M</span> Mini Mağaza
          </NavLink>
          <nav className="nav">
            <NavLink to="/" end className={navClass}>
              Ürünler
            </NavLink>
            <NavLink to="/sepet" className={navClass}>
              Sepet {count > 0 && <span className="nav-badge">{count}</span>}
            </NavLink>
            <NavLink to="/siparisler" className={navClass}>
              Siparişlerim
            </NavLink>
            <NavLink to="/bildirimler" className={navClass}>
              Bildirimler
            </NavLink>
            <NavLink to="/yonetim" className={navClass}>
              Yönetim {isAdmin && <span className="badge badge-blue">Admin</span>}
            </NavLink>
            <NavLink to="/api-dokumani" className={navClass}>
              API Dokümanı
            </NavLink>
          </nav>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            {user ? (
              <>
                <span className="small">
                  <strong>{user.fullName}</strong>{' '}
                  <span className={`badge ${isAdmin ? 'badge-blue' : 'badge-gray'}`}>
                    {user.role} · {user.provider}
                  </span>
                </span>
                <button
                  className="btn btn-sm"
                  onClick={() => {
                    logout()
                    void refreshBasket()
                  }}
                >
                  Çıkış
                </button>
              </>
            ) : (
              <Link to="/giris" className="btn btn-primary btn-sm">
                Giriş Yap / Google
              </Link>
            )}
          </div>
        </div>
      </header>
      <main className="container main">
        <Outlet />
      </main>
      <footer className="footer">
        <div className="container footer-inner">
          <HealthBar />
          <span className="muted small">Mikroservis demo · .NET 10 + React 19</span>
        </div>
      </footer>
    </div>
  )
}
