import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, errorMessage } from '../api'
import { useAuth, useBasket, useToast } from '../context/contexts'
import { useAsync } from '../hooks/useAsync'

declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (cfg: { client_id: string; callback: (resp: { credential: string }) => void }) => void
          renderButton: (el: HTMLElement, opts: Record<string, unknown>) => void
        }
      }
    }
  }
}

export function LoginPage() {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('admin@minimagaza.com')
  const [fullName, setFullName] = useState('')
  const [password, setPassword] = useState('Admin123!')
  const [googleEmail, setGoogleEmail] = useState('ornek.kullanici@gmail.com')
  const [googleName, setGoogleName] = useState('Örnek Google Kullanıcısı')
  const [googleRole, setGoogleRole] = useState<'Customer' | 'Admin'>('Admin')
  const [submitting, setSubmitting] = useState(false)

  const { setSession } = useAuth()
  const { refresh: refreshBasket } = useBasket()
  const toast = useToast()
  const navigate = useNavigate()

  const { data: config } = useAsync(() => api.authConfig(), [])

  // Google Client ID yapılandırılmışsa gerçek Google Identity Services (GIS) butonunu yükle
  useEffect(() => {
    const clientId = config?.googleClientId
    if (!clientId) return

    const script = document.createElement('script')
    script.src = 'https://accounts.google.com/gsi/client'
    script.async = true
    script.onload = () => {
      const btnContainer = document.getElementById('google-gis-btn')
      if (window.google && btnContainer) {
        window.google.accounts.id.initialize({
          client_id: clientId,
          callback: (resp) => {
            void handleGoogleCredential(resp.credential)
          },
        })
        window.google.accounts.id.renderButton(btnContainer, {
          theme: 'outline',
          size: 'large',
          text: 'signin_with',
          locale: 'tr',
        })
      }
    }
    document.body.appendChild(script)
  }, [config?.googleClientId])

  async function handleGoogleCredential(credential: string) {
    setSubmitting(true)
    try {
      const auth = await api.googleLogin({ credential })
      setSession(auth)
      await refreshBasket()
      toast.success(`Google ile giriş yapıldı: ${auth.user.fullName}`)
      void navigate('/')
    } catch (e) {
      toast.error(errorMessage(e))
    } finally {
      setSubmitting(false)
    }
  }

  async function handleSimulatedGoogle(e: React.FormEvent) {
    e.preventDefault()
    setSubmitting(true)
    try {
      const auth = await api.googleLogin({
        email: googleEmail,
        fullName: googleName,
        requestedRole: googleRole,
      })
      setSession(auth)
      await refreshBasket()
      toast.success(`Google hesabıyla giriş yapıldı (${auth.user.role}): ${auth.user.fullName}`)
      void navigate('/')
    } catch (err) {
      toast.error(errorMessage(err))
    } finally {
      setSubmitting(false)
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setSubmitting(true)
    try {
      const auth =
        mode === 'login'
          ? await api.login(email, password)
          : await api.register(email, fullName, password)
      setSession(auth)
      await refreshBasket()
      toast.success(`Hoş geldiniz, ${auth.user.fullName} (${auth.user.role})`)
      void navigate('/')
    } catch (err) {
      toast.error(errorMessage(err))
    } finally {
      setSubmitting(false)
    }
  }

  function fillDemo(acc: { email: string; password: string }) {
    setMode('login')
    setEmail(acc.email)
    setPassword(acc.password)
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Üye Girişi & Google Doğrulaması</h1>
          <p className="muted">
            Üye Veritabanı: <code>{config?.databaseName ?? 'ETicaret_IdentityDb (dbo.Users)'}</code> · Mikroservis:{' '}
            <code>Identity.Api (:5106)</code>
          </p>
        </div>
      </div>

      <div className="grid-2">
        {/* Sol: E-posta/Şifre Giriş veya Kayıt */}
        <div className="card">
          <div style={{ display: 'flex', gap: 8, marginBottom: 14 }}>
            <button
              type="button"
              className={`btn ${mode === 'login' ? 'btn-primary' : ''}`}
              onClick={() => setMode('login')}
            >
              Giriş Yap
            </button>
            <button
              type="button"
              className={`btn ${mode === 'register' ? 'btn-primary' : ''}`}
              onClick={() => setMode('register')}
            >
              Yeni Üye Ol
            </button>
          </div>

          <form onSubmit={(e) => void handleSubmit(e)}>
            {mode === 'register' && (
              <div className="field">
                <label>Ad Soyad</label>
                <input
                  className="input"
                  value={fullName}
                  onChange={(e) => setFullName(e.target.value)}
                  placeholder="Ad Soyad"
                  required
                />
              </div>
            )}

            <div className="field">
              <label>E-posta Adresi</label>
              <input
                type="email"
                className="input"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />
            </div>

            <div className="field">
              <label>Şifre</label>
              <input
                type="password"
                className="input"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </div>

            <button type="submit" className="btn btn-primary" style={{ width: '100%' }} disabled={submitting}>
              {submitting ? 'İşleniyor...' : mode === 'login' ? 'Giriş Yap (JWT)' : 'Kayıt Ol ve Giriş Yap'}
            </button>
          </form>

          <div style={{ marginTop: 18, paddingTop: 14, borderTop: '1px solid var(--border)' }}>
            <div className="small muted" style={{ marginBottom: 8 }}>
              <strong>Hazır Veritabanı Hesapları (Tıklayıp Doldurun):</strong>
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {(config?.demoAccounts ?? [
                { email: 'admin@minimagaza.com', password: 'Admin123!', role: 'Admin', label: 'Sistem Yöneticisi (Admin)' },
                { email: 'musteri@minimagaza.com', password: 'Musteri123!', role: 'Customer', label: 'Standart Müşteri' },
              ]).map((acc) => (
                <button
                  key={acc.email}
                  type="button"
                  className="test-card-btn"
                  onClick={() => fillDemo(acc)}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <strong>{acc.label}</strong>
                    <span className={`badge ${acc.role === 'Admin' ? 'badge-blue' : 'badge-gray'}`}>{acc.role}</span>
                  </div>
                  <div className="small mono muted">
                    {acc.email} / {acc.password}
                  </div>
                </button>
              ))}
            </div>
          </div>
        </div>

        {/* Sağ: Google OAuth 2.0 / OpenID Connect */}
        <div className="card">
          <h3>Google ile Giriş Yap (OAuth 2.0 / OIDC)</h3>
          <p className="muted small" style={{ marginBottom: 14 }}>
            <code>Identity.Api</code> hem gerçek Google Identity Services (<code>id_token</code>) doğrulamasını hem de
            lokal geliştirme ortamında Google OAuth simülasyonunu destekler. Giriş yapan Google hesabı{' '}
            <code>ETicaret_IdentityDb.dbo.Users</code> tablosuna otomatik kaydedilir.
          </p>

          {config?.googleClientId ? (
            <div id="google-gis-btn" style={{ marginBottom: 16 }} />
          ) : (
            <div className="alert small">
              Gerçek Google OAuth butonu için <code>Identity.Api/appsettings.json</code> içindeki{' '}
              <code>GoogleAuth:ClientId</code> alanına kendi Google Cloud Client ID değerinizi yazabilirsiniz. Aşağıdan
              Google OAuth akışını doğrudan simüle edebilirsiniz:
            </div>
          )}

          <form onSubmit={(e) => void handleSimulatedGoogle(e)}>
            <div className="field">
              <label>Google E-posta Adresi</label>
              <input
                type="email"
                className="input"
                value={googleEmail}
                onChange={(e) => setGoogleEmail(e.target.value)}
                required
              />
            </div>

            <div className="field">
              <label>Google Profil Adı</label>
              <input
                className="input"
                value={googleName}
                onChange={(e) => setGoogleName(e.target.value)}
                required
              />
            </div>

            <div className="field">
              <label>İlk Kayıtta Atanacak Yetki (Demo Kolaylığı)</label>
              <select
                className="input"
                value={googleRole}
                onChange={(e) => setGoogleRole(e.target.value as 'Admin' | 'Customer')}
              >
                <option value="Admin">Admin (Yönetim Paneline Tam Yetki)</option>
                <option value="Customer">Customer (Standart Müşteri)</option>
              </select>
            </div>

            <button type="submit" className="btn btn-primary" style={{ width: '100%' }} disabled={submitting}>
              Google Hesabıyla Devam Et
            </button>
          </form>
        </div>
      </div>
    </div>
  )
}

