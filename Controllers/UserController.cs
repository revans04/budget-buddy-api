
using Microsoft.AspNetCore.Mvc;
using FamilyBudgetApi.Services;

namespace FamilyBudgetApi.Controllers;

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly UserService _userService;

    public UserController(UserService userService)
    {
        _userService = userService;
    }

    [HttpGet("ping")]
    public async Task<IActionResult> Ping()
    {
        var response = new
        {
            Message = "pong",
            DateTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
        return Ok(response);
    }

    [HttpGet("{userId}")]
    [AuthorizeFirebase]
    public async Task<IActionResult> GetUser(string userId)
    {
        try
        {
            var user = await _userService.GetUser(userId);
            if (user == null) return NotFound();
            return Ok(user);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetUser: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("by-email/{email}")]
    [AuthorizeFirebase]
    public async Task<IActionResult> GetUserByEmail(string email)
    {
        try
        {
            var user = await _userService.GetUserByEmail(email);
            if (user == null) return NotFound();
            return Ok(user);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetUserByEmail: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{userId}")]
    [AuthorizeFirebase]
    public async Task<IActionResult> SaveUser(string userId, [FromBody] UserData userData)
    {
        try
        {
            await _userService.SaveUser(userId, userData);
            return Ok();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SaveUser: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }
}