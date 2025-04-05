// Controllers/PlaidController.cs
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Mvc;
using Going.Plaid;
using Going.Plaid.Link;
using Going.Plaid.Entity;

namespace FamilyBudgetApi.Controllers
{

    [ApiController]
    [Route("api/plaid")]
    public class PlaidController : ControllerBase
    {
        private readonly PlaidClient _plaidClient;

        public PlaidController(IConfiguration configuration)
        {
            _plaidClient = new PlaidClient(
                clientId: configuration["PlaidClientId"],
                secret: configuration["PlaidSecret"],
                environment: Going.Plaid.Environment.Sandbox
            );
        }

        [HttpGet("link-token")]
        public async Task<IActionResult> GetLinkToken()
        {
            try
            {
                var authHeader = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    return Unauthorized("Invalid Authorization header: Bearer token required");
                }

                var token = authHeader.Substring("Bearer ".Length);
                var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(token);
                var uid = decodedToken.Uid;
                Console.WriteLine($"Authenticated user: {uid}");

                var request = new Going.Plaid.Link.LinkTokenCreateRequest
                {
                    ClientName = "Family Funds",
                    CountryCodes = new List<Going.Plaid.Entity.CountryCode> { CountryCode.Us }.AsReadOnly(),
                    User = new Going.Plaid.Entity.LinkTokenCreateRequestUser { ClientUserId = "user-id" },
                    Products = new List<Going.Plaid.Entity.Products> { Products.Transactions }.AsReadOnly()
                };
                var response = await _plaidClient.LinkTokenCreateAsync(request);
                return Ok(new { LinkToken = response.LinkToken });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLinkToken: {ex.Message}");
                return Unauthorized();
            }
        }
    }
}