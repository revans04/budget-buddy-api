// Controllers/PaymentsController.cs
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace FamilyBudgetApi.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    [HttpPost("stripe")]
    public async Task<IActionResult> ProcessStripePayment([FromBody] PaymentRequest request)
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

            var options = new PaymentIntentCreateOptions
            {
                Amount = request.Amount,
                Currency = "usd",
                PaymentMethod = request.PaymentMethodId,
                ConfirmationMethod = "manual",
                Confirm = true
            };
            var service = new PaymentIntentService();
            var paymentIntent = await service.CreateAsync(options);

            return Ok(new { PaymentIntentId = paymentIntent.Id });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ProcessStripePayment: {ex.Message}");
            return Unauthorized();
        }
    }
}

public record PaymentRequest(int Amount, string PaymentMethodId);