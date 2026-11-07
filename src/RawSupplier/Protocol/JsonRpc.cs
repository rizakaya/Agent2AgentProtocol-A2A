using System.Text.Json;
using System.Text.Json.Serialization;

namespace RawSupplier.Protocol;

// =====================================================================
// JSON-RPC 2.0: A2A'nın JSON-RPC binding'i bu basit zarfı kullanır.
//
// İstek:   { "jsonrpc": "2.0", "id": 1, "method": "SendMessage", "params": { ... } }
// Başarı:  { "jsonrpc": "2.0", "id": 1, "result": { ... } }
// Hata:    { "jsonrpc": "2.0", "id": 1, "error": { "code": -32601, "message": "..." } }
//
// "id" istemcinin verdiği değerdir; sunucu cevapta aynen geri döner.
// Böylece istemci hangi cevabın hangi isteğe ait olduğunu bilir.
// =====================================================================

public sealed class JsonRpcResponse
{
    public string Jsonrpc { get; init; } = "2.0";

    /// <summary>İsteğin id'si. String veya sayı olabilir, o yüzden JsonElement. Okunamadıysa null.</summary>
    /// <remarks>JSON-RPC kuralı: id okunamadıysa bile cevapta "id": null olarak yer almalı, bu yüzden Never.</remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public JsonElement? Id { get; init; }

    public object? Result { get; init; }
    public JsonRpcError? Error { get; init; }

    public static JsonRpcResponse Success(JsonElement? id, object result) => new() { Id = id, Result = result };

    public static JsonRpcResponse Failure(JsonElement? id, int code, string message) =>
        new() { Id = id, Error = new JsonRpcError(code, message) };
}

public sealed record JsonRpcError(int Code, string Message);

/// <summary>
/// Hata kodları.
/// -32700 … -32600 aralığı JSON-RPC 2.0 standardından gelir.
/// -32001 … -32009 aralığı A2A'ya özeldir.
/// </summary>
public static class ErrorCodes
{
    // --- JSON-RPC 2.0 standart kodları ---
    public const int ParseError = -32700;      // Gövde geçerli JSON değil
    public const int InvalidRequest = -32600;  // JSON geçerli ama JSON-RPC zarfı bozuk
    public const int MethodNotFound = -32601;  // Böyle bir metod yok
    public const int InvalidParams = -32602;   // "params" beklenen şekilde değil
    public const int InternalError = -32603;   // Sunucu tarafında beklenmeyen hata

    // --- A2A'ya özel kodlar (Faz 1'de kullandıklarımız) ---
    public const int TaskNotFound = -32001;              // Faz 3'te göreceğiz
    public const int ContentTypeNotSupported = -32005;   // Desteklenmeyen içerik türü
    public const int VersionNotSupported = -32009;       // İstemcinin istediği A2A sürümü desteklenmiyor
}

/// <summary>A2A v1.0'daki metod adları. Faz 1'de sadece SendMessage'ı uyguluyoruz.</summary>
public static class Methods
{
    public const string SendMessage = "SendMessage";
    public const string SendStreamingMessage = "SendStreamingMessage"; // Faz 3
    public const string GetTask = "GetTask";                           // Faz 3
    public const string CancelTask = "CancelTask";                     // Faz 3
}
