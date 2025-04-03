// Filters/AuthorizeFirebaseAttribute.cs
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Threading.Tasks;

public class AuthorizeFirebaseAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var authHeader = context.HttpContext.Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            context.Result = new UnauthorizedObjectResult("Invalid Authorization header: Bearer token required");
            return;
        }

        var token = authHeader.Substring("Bearer ".Length);
        try
        {
            var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(token);
            context.HttpContext.Items["UserId"] = decodedToken.Uid;
            await next();
        }
        catch (Exception ex)
        {
            context.Result = new UnauthorizedObjectResult($"Token verification failed: {ex.Message}");
        }
    }
}