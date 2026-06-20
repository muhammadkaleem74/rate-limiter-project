using Microsoft.AspNetCore.Mvc;

namespace RateLimiterApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DataController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() =>
        Ok(new { message = "Data retrieved successfully", timestamp = DateTime.UtcNow, method = "GET" });

    [HttpPost]
    public IActionResult Post([FromBody] object? payload) =>
        Ok(new { message = "Data created successfully", timestamp = DateTime.UtcNow, method = "POST" });

    [HttpPut("{id}")]
    public IActionResult Put(int id, [FromBody] object? payload) =>
        Ok(new { message = $"Data {id} updated", timestamp = DateTime.UtcNow, method = "PUT" });

    [HttpDelete("{id}")]
    public IActionResult Delete(int id) =>
        Ok(new { message = $"Data {id} deleted", timestamp = DateTime.UtcNow, method = "DELETE" });
}
