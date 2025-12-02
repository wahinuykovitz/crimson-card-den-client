using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Simple HTTP client wrapper for talking to the 500 backend.
/// Attach this as a component (e.g. on GameSystems) and reference it from GameController.
/// </summary>
public class BackendClient : MonoBehaviour
{
    [Header("Backend config")]
    [Tooltip("Base URL for the backend API, e.g. http://localhost:8080")]
    [SerializeField] private string baseUrl = "http://localhost:8080";

    [Tooltip("Log requests and responses to Unity console for debugging.")]
    [SerializeField] private bool logRequests = true;

    private HttpClient http;

    private void Awake()
    {
        http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private string BuildUrl(string path)
    {
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private async Task<T> PostJsonAsync<T>(string path, object body)
    {
        var url = BuildUrl(path);
        var json = JsonConvert.SerializeObject(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        if (logRequests)
        {
            Debug.Log($"[BackendClient] POST {url}\nBody: {json}");
        }

        using var response = await http.PostAsync(url, content);
        var respText = await response.Content.ReadAsStringAsync();

        if (logRequests)
        {
            Debug.Log($"[BackendClient] Response {response.StatusCode} from {url}\n{respText}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(
                $"Backend error {response.StatusCode} at {url}: {respText}");
        }

        if (typeof(T) == typeof(VoidResponse))
        {
            return default;
        }

        return JsonConvert.DeserializeObject<T>(respText);
    }

    private sealed class VoidResponse { }

    // ---------- Public API used by GameController ----------

    /// <summary>
    /// Simple local deal demo endpoint. Returns one-off hands.
    /// Backend route: POST /api/games/500/host-local
    /// Body: { playerCount }
    /// </summary>
    public Task<DealResponse> HostLocalDealAsync(int playerCount)
    {
        return PostJsonAsync<DealResponse>(
            "/api/games/500/host-local",
            new { playerCount });
    }

    /// <summary>
    /// Creates a full local 4-player session.
    /// Backend route: POST /api/games/500/sessions/host-local
    /// Body: { playerCount }
    /// </summary>
    public Task<SessionResponse> HostLocalSessionAsync(int playerCount)
    {
        return PostJsonAsync<SessionResponse>(
            "/api/games/500/sessions/host-local",
            new { playerCount });
    }

    /// <summary>
    /// Places a bid in the current session.
    /// Backend route: POST /api/games/500/sessions/{sessionId}/bidding/bid
    /// Body: { seatIndex, tricks, trump }
    /// </summary>
    public Task<SessionResponse> PlaceBidAsync(
        string sessionId,
        int seatIndex,
        int tricks,
        string trump)
    {
        return PostJsonAsync<SessionResponse>(
            $"/api/games/500/sessions/{sessionId}/bidding/bid",
            new
            {
                seatIndex,
                tricks,
                trump
            });
    }

    /// <summary>
    /// Passes during bidding.
    /// Backend route: POST /api/games/500/sessions/{sessionId}/bidding/pass
    /// Body: { seatIndex }
    /// </summary>
    public Task<SessionResponse> PassAsync(
        string sessionId,
        int seatIndex)
    {
        return PostJsonAsync<SessionResponse>(
            $"/api/games/500/sessions/{sessionId}/bidding/pass",
            new
            {
                seatIndex
            });
    }

    /// <summary>
    /// Reveals kitty for current contract winner.
    /// Backend route: POST /api/games/500/sessions/{sessionId}/kitty/reveal
    /// Body: { }
    /// </summary>
    public Task<SessionResponse> RevealKittyAsync(
        string sessionId)
    {
        return PostJsonAsync<SessionResponse>(
            $"/api/games/500/sessions/{sessionId}/kitty/reveal",
            new { });
    }

    /// <summary>
    /// Plays a card from given seat's hand.
    /// Backend route: POST /api/games/500/sessions/{sessionId}/play-card
    /// Body: { seatIndex, cardIndex }
    /// </summary>
    public Task<SessionResponse> PlayCardAsync(
        string sessionId,
        int seatIndex,
        int cardIndex)
    {
        return PostJsonAsync<SessionResponse>(
            $"/api/games/500/sessions/{sessionId}/play-card",
            new
            {
                seatIndex,
                cardIndex
            });
    }
}
