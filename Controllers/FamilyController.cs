using Microsoft.AspNetCore.Mvc;
using FamilyBudgetApi.Services;
using FamilyBudgetApi.Models;
using Google.Cloud.Firestore;
using Newtonsoft.Json;
using System;
using System.Configuration;
using System.Threading.Tasks;

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
            try
            {
                var family = await _familyService.GetUserFamily(uid);
                return family != null ? Ok(family) : Ok(null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetUserFamily: {ex.Message}");
                return StatusCode(500, new { Error = $"Failed to get family: {ex.Message}" });
            }
        }

        [HttpPost("create")]
        [AuthorizeFirebase]
        public async Task<IActionResult> CreateFamily([FromBody] CreateFamilyRequest request)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            if (string.IsNullOrEmpty(request.Name))
                return BadRequest(new { Error = "Family name is required" });

            try
            {
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
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CreateFamily: {ex.Message}");
                return StatusCode(500, new { Error = $"Failed to create family: {ex.Message}" });
            }
        }

        [HttpPost("{familyId}/members")]
        [AuthorizeFirebase]
        public async Task<IActionResult> AddFamilyMember(string familyId, [FromBody] UserRef member)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var family = await _familyService.GetFamilyById(familyId);
            if (family == null || family.OwnerUid != uid)
                return Unauthorized("Only the family owner can add members");

            try
            {
                await _familyService.AddFamilyMember(familyId, member);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddFamilyMember: {ex.Message}");
                return StatusCode(500, new { Error = $"Failed to add family member: {ex.Message}" });
            }
        }

        [HttpDelete("{familyId}/members/{memberUid}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> RemoveFamilyMember(string familyId, string memberUid)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var family = await _familyService.GetFamilyById(familyId);
            if (family == null || family.OwnerUid != uid)
                return Unauthorized("Only the family owner can remove members");

            try
            {
                await _familyService.RemoveFamilyMember(familyId, memberUid);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RemoveFamilyMember: {ex.Message}");
                return StatusCode(500, new { Error = $"Failed to remove family member: {ex.Message}" });
            }
        }

        [HttpPut("{familyId}/rename")]
        [AuthorizeFirebase]
        public async Task<IActionResult> RenameFamily(string familyId, [FromBody] RenameFamilyRequest request)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            if (string.IsNullOrEmpty(request.Name))
                return BadRequest(new { Error = "Family name is required" });

            var family = await _familyService.GetFamilyById(familyId);
            if (family == null || family.OwnerUid != uid)
                return Unauthorized("Only the family owner can rename the family");

            try
            {
                await _familyService.RenameFamily(familyId, request.Name);
                return Ok(new { Message = "Family renamed successfully", Name = request.Name });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RenameFamily: {ex.Message}");
                return StatusCode(500, new { Error = $"Failed to rename family: {ex.Message}" });
            }
        }

        [HttpPost("{familyId}/entities")]
        [AuthorizeFirebase]
        public async Task<IActionResult> CreateEntity(string familyId, [FromBody] Entity entity)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var family = await _familyService.GetFamilyById(familyId);
            if (family == null || family.OwnerUid != uid)
                return Unauthorized("Only the family owner can create entities");

            // Validate EntityType
            if (!Enum.TryParse<EntityType>(entity.Type, true, out _))
                return BadRequest(new { Error = $"Invalid entity type: {entity.Type}. Must be one of: {string.Join(", ", Enum.GetNames(typeof(EntityType)))}" });

            try
            {
                entity.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
                entity.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
                await _familyService.CreateEntity(familyId, entity);
                return Ok(new { EntityId = entity.Id });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CreateEntity: {ex.Message}");
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPut("{familyId}/entities/{entityId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> UpdateEntity(string familyId, string entityId, [FromBody] Entity entity)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var family = await _familyService.GetFamilyById(familyId);
            if (family == null || family.OwnerUid != uid)
                return Unauthorized("Only the family owner can update entities");

            // Validate EntityType
            if (!Enum.TryParse<EntityType>(entity.Type, true, out _))
                return BadRequest(new { Error = $"Invalid entity type: {entity.Type}. Must be one of: {string.Join(", ", Enum.GetNames(typeof(EntityType)))}" });

            entity.Id = entityId;
            entity.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
            try
            {
                await _familyService.UpdateEntity(familyId, entity);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateEntity: {ex.Message} {ex.StackTrace}");
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpDelete("{familyId}/entities/{entityId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> DeleteEntity(string familyId, string entityId)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var family = await _familyService.GetFamilyById(familyId);
            if (family == null || family.OwnerUid != uid)
                return Unauthorized("Only the family owner can delete entities");

            try
            {
                await _familyService.DeleteEntity(familyId, entityId);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteEntity: {ex.Message}");
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPost("{familyId}/entities/{entityId}/members")]
        [AuthorizeFirebase]
        public async Task<IActionResult> AddEntityMember(string familyId, string entityId, [FromBody] UserRef member)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var family = await _familyService.GetFamilyById(familyId);
            if (family == null || family.OwnerUid != uid)
                return Unauthorized("Only the family owner can add entity members");

            try
            {
                await _familyService.AddEntityMember(familyId, entityId, member);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddEntityMember: {ex.Message}");
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpDelete("{familyId}/entities/{entityId}/members/{memberUid}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> RemoveEntityMember(string familyId, string entityId, string memberUid)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var family = await _familyService.GetFamilyById(familyId);
            if (family == null || family.OwnerUid != uid)
                return Unauthorized("Only the family owner can remove entity members");

            try
            {
                await _familyService.RemoveEntityMember(familyId, entityId, memberUid);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RemoveEntityMember: {ex.Message}");
                return BadRequest(new { Error = ex.Message });
            }
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

            try
            {
                await _familyService.CreatePendingInvite(pendingInvite);
                var inviteLink = $"{_baseUrl}/accept-invite?token={token}";
                await _brevoService.SendInviteEmail(pendingInvite.InviteeEmail, family.Name, inviteLink);
                return Ok(new { Message = "Invite sent", Token = token });
            }
            catch (Exception ex)
            {
                await _familyService.DeletePendingInvite(token);
                Console.WriteLine($"Error in InviteUser: {ex.Message}");
                return StatusCode(500, new { Error = $"Failed to send invite email: {ex.Message}" });
            }
        }

        [HttpPost("accept-invite")]
        [AuthorizeFirebase]
        public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            var email = HttpContext.Items["Email"]?.ToString();
            var pendingInvite = await _familyService.GetPendingInviteByToken(request.Token);
            if (pendingInvite == null || pendingInvite.ExpiresAt.ToDateTime() < DateTime.UtcNow)
                return BadRequest(new { Error = "Invalid or expired invite" });

            if (pendingInvite.InviteeEmail != email.ToLower().Trim())
                return Unauthorized("This invite is not for you");

            var family = await _familyService.GetUserFamily(pendingInvite.InviterUid);
            if (family == null)
                return BadRequest(new { Error = "Family not found" });

            try
            {
                var member = new UserRef { Uid = uid, Email = email };
                await _familyService.AddFamilyMember(family.Id, member);
                await _familyService.DeletePendingInvite(pendingInvite.Token);
                await _familyService.UpdateLastAccessed(uid);
                return Ok(new { FamilyId = family.Id });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AcceptInvite: {ex.Message}");
                return StatusCode(500, new { Error = $"Failed to accept invite: {ex.Message}" });
            }
        }

        [HttpGet("pending-invites/{inviterUid}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetPendingInvites(string inviterUid)
        {
            var uid = HttpContext.Items["UserId"]?.ToString();
            if (uid != inviterUid)
                return Unauthorized("Can only view your own pending invites");

            try
            {
                var invites = await _familyService.GetPendingInvitesByInviter(inviterUid);
                return Ok(invites);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPendingInvites: {ex.Message}");
                return StatusCode(500, new { Error = $"Failed to get pending invites: {ex.Message}" });
            }
        }

        [HttpGet("last-accessed/{uid}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetLastAccessed(string uid)
        {
            try
            {
                var lastAccessed = await _familyService.GetLastAccessed(uid);
                return Ok(new { LastAccessed = lastAccessed?.ToDateTime() });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLastAccessed: {ex.Message}");
                return StatusCode(500, new { Error = $"Failed to get last accessed: {ex.Message}" });
            }
        }

        public class RenameFamilyRequest
        {
            public string Name { get; set; }
        }
    }
}
