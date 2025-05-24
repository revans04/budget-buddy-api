using Google.Cloud.Firestore;
using FamilyBudgetApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;

namespace FamilyBudgetApi.Services
{
    public class FamilyService
    {
        private readonly FirestoreDb _db;

        public FamilyService(FirestoreDb db)
        {
            _db = db;
        }

        public async Task<Family> GetUserFamily(string uid)
        {
            var familyRef = _db.Collection("families").WhereArrayContains("memberUids", uid);
            var snapshot = await familyRef.GetSnapshotAsync();
            return snapshot.Documents.Select(doc => doc.ConvertTo<Family>()).FirstOrDefault();
        }

        public async Task<Family> GetFamilyById(string familyId)
        {
            var doc = await _db.Collection("families").Document(familyId).GetSnapshotAsync();
            return doc.Exists ? doc.ConvertTo<Family>() : null;
        }

        public async Task CreateFamily(string familyId, Family family)
        {
            await _db.Collection("families").Document(familyId).SetAsync(family);
        }

        public async Task AddFamilyMember(string familyId, UserRef member)
        {
            var familyRef = _db.Collection("families").Document(familyId);
            await familyRef.UpdateAsync(new Dictionary<string, object>
            {
                { "members", FieldValue.ArrayUnion(member) },
                { "memberUids", FieldValue.ArrayUnion(member.Uid) },
                { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
            });
        }

        public async Task RemoveFamilyMember(string familyId, string memberUid)
        {
            var familyRef = _db.Collection("families").Document(familyId);
            var family = await GetFamilyById(familyId);
            if (family == null) return;

            var memberToRemove = family.Members.FirstOrDefault(m => m.Uid == memberUid);
            if (memberToRemove != null)
            {
                await familyRef.UpdateAsync(new Dictionary<string, object>
                {
                    { "members", FieldValue.ArrayRemove(memberToRemove) },
                    { "memberUids", FieldValue.ArrayRemove(memberUid) },
                    { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                });
            }
        }

        public async Task RenameFamily(string familyId, string newName)
        {
            var familyRef = _db.Collection("families").Document(familyId);
            var family = await GetFamilyById(familyId);
            if (family == null)
                throw new Exception($"Family {familyId} not found");

            await familyRef.UpdateAsync(new Dictionary<string, object>
            {
                { "name", newName },
                { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
            });
        }

        public async Task CreateEntity(string familyId, Entity entity)
        {
            var familyRef = _db.Collection("families").Document(familyId);
            var family = await GetFamilyById(familyId);
            if (family == null) throw new Exception($"Family {familyId} not found");

            entity.Id ??= Guid.NewGuid().ToString();
            foreach (var member in entity.Members)
            {
                if (!family.MemberUids.Contains(member.Uid))
                    throw new Exception($"Member {member.Uid} is not part of family {familyId}");
            }

            // Validate EntityType
            if (!Enum.TryParse<EntityType>(entity.Type, true, out _))
                throw new Exception($"Invalid entity type: {entity.Type}. Must be one of: {string.Join(", ", Enum.GetNames(typeof(EntityType)))}");

            await familyRef.UpdateAsync(new Dictionary<string, object>
            {
                { "entities", FieldValue.ArrayUnion(entity) },
                { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
            });
        }

        public async Task UpdateEntity(string familyId, Entity entity)
        {
            var familyRef = _db.Collection("families").Document(familyId);
            var family = await GetFamilyById(familyId);
            if (family == null) throw new Exception($"Family {familyId} not found");

            var existingEntity = family.Entities.FirstOrDefault(e => e.Id == entity.Id);
            if (existingEntity == null)
            {
                await this.CreateEntity(familyId, entity);
            }
            else
            {
                foreach (var member in entity.Members)
                {
                    if (!family.MemberUids.Contains(member.Uid))
                        throw new Exception($"Member {member.Uid} is not part of family {familyId}");
                }

                // Validate EntityType
                if (!Enum.TryParse<EntityType>(entity.Type, true, out _))
                    throw new Exception($"Invalid entity type: {entity.Type}. Must be one of: {string.Join(", ", Enum.GetNames(typeof(EntityType)))}");

                // Merge the incoming entity with the existing entity
                var updatedEntity = MergeEntities(existingEntity, entity);

                // Update the entities array by replacing the matching entity
                var updatedEntities = family.Entities
                    .Select(e => e.Id == entity.Id ? updatedEntity : e)
                    .ToList();

                await _db.RunTransactionAsync(async transaction =>
                {
                    transaction.Update(familyRef, new Dictionary<string, object>
                    {
                        { "entities", updatedEntities },
                        { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                    });
                });
            }

        }

        private Entity MergeEntities(Entity existing, Entity incoming)
        {
            var updated = new Entity();
            var properties = typeof(Entity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<FirestorePropertyAttribute>() != null); // Only include Firestore properties

            foreach (var prop in properties)
            {
                var incomingValue = prop.GetValue(incoming);
                var existingValue = prop.GetValue(existing);

                // Use incoming value if not null, otherwise existing value
                var valueToSet = incomingValue ?? existingValue;

                // Special handling for collections to avoid null or empty overwrites
                if (valueToSet != null && prop.PropertyType.IsGenericType &&
                    prop.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    // Only overwrite if the incoming collection is non-empty
                    if (incomingValue != null && ((System.Collections.IList)incomingValue).Count > 0)
                    {
                        prop.SetValue(updated, incomingValue);
                    }
                    else
                    {
                        prop.SetValue(updated, existingValue);
                    }
                }
                else
                {
                    prop.SetValue(updated, valueToSet);
                }
            }

            // Ensure critical fields are preserved or set
            updated.Id = existing.Id; // Always preserve the original ID
            updated.CreatedAt = existing.CreatedAt; // Preserve original creation time

            // Ensure TemplateBudget defaults
            if (updated.TemplateBudget != null)
            {
                updated.TemplateBudget.Categories ??= new List<BudgetCategory>();
            }

            return updated;
        }

        public async Task DeleteEntity(string familyId, string entityId)
        {
            var familyRef = _db.Collection("families").Document(familyId);
            var family = await GetFamilyById(familyId);
            if (family == null) throw new Exception($"Family {familyId} not found");

            var entityToRemove = family.Entities.FirstOrDefault(e => e.Id == entityId);
            if (entityToRemove == null) throw new Exception($"Entity {entityId} not found");

            await familyRef.UpdateAsync(new Dictionary<string, object>
            {
                { "entities", FieldValue.ArrayRemove(entityToRemove) },
                { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
            });
        }

        public async Task AddEntityMember(string familyId, string entityId, UserRef member)
        {
            var familyRef = _db.Collection("families").Document(familyId);
            var family = await GetFamilyById(familyId);
            if (family == null) throw new Exception($"Family {familyId} not found");

            var entity = family.Entities.FirstOrDefault(e => e.Id == entityId);
            if (entity == null) throw new Exception($"Entity {entityId} not found");

            if (!family.MemberUids.Contains(member.Uid))
                throw new Exception($"Member {member.Uid} is not part of family {familyId}");

            var updatedEntity = new Entity
            {
                Id = entity.Id,
                Name = entity.Name,
                Type = entity.Type,
                Members = new List<UserRef>(entity.Members) { member }
            };

            await _db.RunTransactionAsync(async transaction =>
            {
                transaction.Update(familyRef, new Dictionary<string, object>
                {
                    { "entities", FieldValue.ArrayRemove(entity) },
                    { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                });
                transaction.Update(familyRef, new Dictionary<string, object>
                {
                    { "entities", FieldValue.ArrayUnion(updatedEntity) },
                    { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                });
            });
        }

        public async Task RemoveEntityMember(string familyId, string entityId, string memberUid)
        {
            var familyRef = _db.Collection("families").Document(familyId);
            var family = await GetFamilyById(familyId);
            if (family == null) throw new Exception($"Family {familyId} not found");

            var entity = family.Entities.FirstOrDefault(e => e.Id == entityId);
            if (entity == null) throw new Exception($"Entity {entityId} not found");

            var memberToRemove = entity.Members.FirstOrDefault(m => m.Uid == memberUid);
            if (memberToRemove == null) throw new Exception($"Member {memberUid} not found in entity {entityId}");

            var updatedEntity = new Entity
            {
                Id = entity.Id,
                Name = entity.Name,
                Type = entity.Type,
                Members = entity.Members.Where(m => m.Uid != memberUid).ToList()
            };

            await _db.RunTransactionAsync(async transaction =>
            {
                transaction.Update(familyRef, new Dictionary<string, object>
                {
                    { "entities", FieldValue.ArrayRemove(entity) },
                    { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                });
                transaction.Update(familyRef, new Dictionary<string, object>
                {
                    { "entities", FieldValue.ArrayUnion(updatedEntity) },
                    { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                });
            });
        }

        public async Task CreatePendingInvite(PendingInvite invite)
        {
            var inviteRef = _db.Collection("pendingInvites").Document(invite.Token);
            await inviteRef.SetAsync(invite);
        }

        public async Task<PendingInvite> GetPendingInviteByToken(string token)
        {
            var doc = await _db.Collection("pendingInvites").Document(token).GetSnapshotAsync();
            return doc.Exists ? doc.ConvertTo<PendingInvite>() : null;
        }

        public async Task DeletePendingInvite(string token)
        {
            await _db.Collection("pendingInvites").Document(token).DeleteAsync();
        }

        public async Task<List<PendingInvite>> GetPendingInvitesByInviter(string inviterUid)
        {
            var query = _db.Collection("pendingInvites").WhereEqualTo("inviterUid", inviterUid);
            var snapshot = await query.GetSnapshotAsync();
            return snapshot.Documents.Select(doc => doc.ConvertTo<PendingInvite>()).ToList();
        }

        public async Task UpdateLastAccessed(string uid)
        {
            await _db.Collection("users").Document(uid).SetAsync(
                new { LastAccessed = Timestamp.FromDateTime(DateTime.UtcNow) },
                SetOptions.MergeAll
            );
        }

        public async Task<Timestamp?> GetLastAccessed(string uid)
        {
            var userDoc = await _db.Collection("users").Document(uid).GetSnapshotAsync();
            return userDoc.Exists && userDoc.ContainsField("lastAccessed")
                ? userDoc.GetValue<Timestamp>("lastAccessed")
                : null;
        }
    }
}
