namespace BuildingBlocks.Web;

/// <summary> İş kuralı veya doğrulama ihlallerini 400/404/409 olarak döndürmek için kullanılan istisna. </summary>
public sealed class DomainException(string message, int statusCode = 400, IDictionary<string, string[]>? errors = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public IDictionary<string, string[]>? Errors { get; } = errors;
}

/// <summary> Hata simülasyonu (Chaos) tarafından fırlatılan geçici servis hatası (503). </summary>
public sealed class SimulatedFaultException(string message) : Exception(message);

