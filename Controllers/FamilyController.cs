using Microsoft.AspNetCore.Mvc;
using FamilyBudgetApi.Services;
using FamilyBudgetApi.Models;
using Google.Cloud.Firestore;

namespace FamilyBudgetApi.Controllers
{

    [ApiController]
    [Route("api/family")]
    public class FamilyController : ControllerBase
    {
        private readonly FamilyService _familyService;

        public FamilyController(FamilyService familyService)
        {
            _familyService = familyService;
        }

        [HttpGet("{uid}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetUserFamily(string uid)
        {
            var family = await _familyService.GetUserFamily(uid);
            return Ok(family);
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
    }
}
