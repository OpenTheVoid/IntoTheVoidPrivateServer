using Microsoft.AspNetCore.Mvc;
using IntoTheVoidServer.Pomelo;
using IntoTheVoidServer.Router;
using Serilog;

namespace IntoTheVoidServer.Http;

[ApiController]
[Route("admin/api")]
public class AdminApiController : ControllerBase
{
    private readonly MessageRouter _router;
    private readonly PlayerSessionManager _sessionManager;

    public AdminApiController(MessageRouter router, PlayerSessionManager sessionManager)
    {
        _router = router;
        _sessionManager = sessionManager;
    }

    [HttpGet("gm")]
    public async Task<IActionResult> SendGMCommand([FromQuery] string route)
    {
        Log.Information("[Admin] GM command: route={Route}", route);
        try
        {
            var result = await _router.HandleAsync(route, null, MessageType.Request);
            var preview = result != null && result.Length > 0
                ? BitConverter.ToString(result.Take(Math.Min(10, result.Length)).ToArray()).Replace("-", " ")
                : "(empty)";
            return Ok(new { route = route, size = result?.Length ?? 0, preview = preview, success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { route = route, success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// GM: grant currency to the (online) player.
    /// GET /admin/api/gm/addcurrency?type=21&amp;count=8000
    /// type follows TPSData.Currency: 1=Gold, 2=Diamond(垄金), 14=BoundDiamond, 21=CrystalCredit(晶卷)
    /// Persists into GameState (survives re-login) and pushes a live
    /// CurrencyDataPush to connected clients so the HUD updates immediately.
    /// </summary>
    [HttpGet("gm/addcurrency")]
    public async Task<IActionResult> AddCurrency([FromQuery] int type, [FromQuery] int count)
    {
        try
        {
            var newTotal = IntoTheVoidServer.GameState.AddCurrency(type, count);
            var pushData = ProtoBuilder.BuildCurrencyPush((type, newTotal));
            await Net.PomeloTcpServer.PushToAllClientsAsync("gate.CurrencyDataPush", pushData);
            Log.Information("[Admin] GM addcurrency: type={Type}, count={Count}, newTotal={Total}", type, count, newTotal);
            return Ok(new
            {
                success = true,
                type = type,
                added = count,
                newTotal = newTotal,
                pushedToOnlineClients = true
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Admin] GM addcurrency failed");
            return Ok(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("player")]
    public IActionResult PlayerInfo()
    {
        return Ok(new
        {
            playerId = _sessionManager.CurrentPlayerId,
            isLoggedIn = _sessionManager.IsLoggedIn,
            dataKeys = _sessionManager.PlayerData.Keys.ToList()
        });
    }
}
