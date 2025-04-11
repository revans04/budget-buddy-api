using Microsoft.AspNetCore.Mvc;
using FamilyBudgetApi.Services;
using FamilyBudgetApi.Models;
using Google.Cloud.Firestore;
using Newtonsoft.Json;
using System.Configuration;

namespace FamilyBudgetApi.Controllers
{
    [ApiController]
    [Route("api/family")]
    public class FamilyController : ControllerBase
    {
        private readonly FamilyService _familyService;
        private readonly BrevoService _brevoService;
        private readonly string _baseUrl;

        public FamilyController(FamilyService familyService, BrevoService brevoService, IConfiguration configuration)
        {
            _familyService = familyService;
            _brevoService = brevoService;
            _baseUrl = configuration["BaseUrl"];
        }

        [HttpGet("{uid}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetUserFamily(string uid)
        {
            var family = await _familyService.GetUserFamily(uid);
            return family != null ? Ok(family) : Ok(null); // Return null JSON instead of 404
        }

        [HttpPost("create")]
        [AuthorizeFirebase]
        public async Task<IActionResult> CreateFamily([FromBody] CreateFamilyRequest request)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            if (string.IsNullOrEmpty(request.Name))
                return BadRequest(new { Error = "Family name is required" });

            var familyId = Guid.NewGuid().ToString();
            var family = new Family
            {
                Id = familyId,
                Name = request.Name,
                OwnerUid = uid,
                Members = new List<UserRef> { new UserRef { Uid = uid, Email = request.Email } },
                MemberUids = new List<string> { uid },
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            await _familyService.CreateFamily(familyId, family);
            return Ok(new { FamilyId = familyId, Name = family.Name });
        }

        [HttpPost("{familyId}/members")]
        [AuthorizeFirebase]
        public async Task<IActionResult> AddFamilyMember(string familyId, [FromBody] UserRef member)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var family = await _familyService.GetFamilyById(familyId);
            if (family == null || family.OwnerUid != uid)
                return Unauthorized("Only the family owner can add members");

            await _familyService.AddFamilyMember(familyId, member);
            return Ok();
        }

        [HttpDelete("{familyId}/members/{memberUid}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> RemoveFamilyMember(string familyId, string memberUid)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var family = await _familyService.GetFamilyById(familyId);
            if (family == null || family.OwnerUid != uid)
                return Unauthorized("Only the family owner can remove members");

            await _familyService.RemoveFamilyMember(familyId, memberUid);
            return Ok();
        }

        [HttpPost("invite")]
        [AuthorizeFirebase]
        public async Task<IActionResult> InviteUser([FromBody] InviteRequest request)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var family = await _familyService.GetUserFamily(uid);
            if (family == null || family.OwnerUid != uid)
                return Unauthorized("Only the family owner can invite users");

            var token = Guid.NewGuid().ToString();
            var pendingInvite = new PendingInvite
            {
                InviterUid = uid,
                InviterEmail = HttpContext.Items["Email"]?.ToString() ?? "no-reply@budgetapp.com",
                InviteeEmail = request.InviteeEmail.ToLower().Trim(),
                Token = token,
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                ExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(7))
            };

            await _familyService.CreatePendingInvite(pendingInvite);

            try
            {
                var inviteLink = $"{_baseUrl}/accept-invite?token={token}"; // Update to your real URL
                await _brevoService.SendInviteEmail(pendingInvite.InviteeEmail, family.Name, inviteLink);
            }
            catch (Exception ex)
            {
                await _familyService.DeletePendingInvite(token); // Rollback on failure
                return StatusCode(500, new { Error = $"Failed to send invite email: {ex.Message}" });
            }

            return Ok(new { Message = "Invite sent", Token = token });
        }

        [HttpPost("accept-invite")]
        [AuthorizeFirebase]
        public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var email = HttpContext.Items["Email"]?.ToString();
            Console.WriteLine("Contect.Items=" + JsonConvert.SerializeObject(HttpContext.Items));
            var pendingInvite = await _familyService.GetPendingInviteByToken(request.Token);
            if (pendingInvite == null || pendingInvite.ExpiresAt.ToDateTime() < DateTime.UtcNow)
                return BadRequest(new { Error = "Invalid or expired invite" });

            if (pendingInvite.InviteeEmail != email.ToLower().Trim())
                return Unauthorized("This invite is not for you");

            var family = await _familyService.GetUserFamily(pendingInvite.InviterUid);
            if (family == null)
                return BadRequest(new { Error = "Family not found" });

            var member = new UserRef { Uid = uid, Email = email };
            await _familyService.AddFamilyMember(family.Id, member);
            await _familyService.DeletePendingInvite(pendingInvite.Token);
            await _familyService.UpdateLastAccessed(uid); // Update on join

            return Ok(new { FamilyId = family.Id });
        }

        [HttpGet("pending-invites/{inviterUid}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetPendingInvites(string inviterUid)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            if (uid != inviterUid)
                return Unauthorized("Can only view your own pending invites");

            var invites = await _familyService.GetPendingInvitesByInviter(inviterUid);
            return Ok(invites);
        }

        [HttpGet("last-accessed/{uid}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetLastAccessed(string uid)
        {
            var lastAccessed = await _familyService.GetLastAccessed(uid);
            return Ok(new { LastAccessed = lastAccessed?.ToDateTime() });
        }
    }
}