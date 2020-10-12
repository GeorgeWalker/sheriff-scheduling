﻿using SS.Api.infrastructure.authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace SS.Api.helpers.extensions
{
    public static class ClaimExtensions
    {
        public static string GetValueByType(this IEnumerable<Claim> claims, string type) =>
            claims.FirstOrDefault(c => c.Type == type)?.Value;

        public static string GetIdirUserName(this IEnumerable<Claim> claims) => 
            claims.GetValueByType("preferred_username").Replace("@idir", "");

        public static Guid GetIdirId(this IEnumerable<Claim> claims) =>
            Guid.Parse(claims.GetValueByType("idir_userid"));

        public static Guid GetKeyCloakId(this IEnumerable<Claim> claims) =>
            Guid.Parse(claims.GetValueByType(ClaimTypes.NameIdentifier));

        public static bool HasPermissions(this ClaimsPrincipal user, params string[] permissions) =>
            user.HasClaim(c => c.Type == CustomClaimTypes.Permission) && permissions.All(perm => user.HasClaim(CustomClaimTypes.Permission, perm));

        public static bool HasPermission(this ClaimsPrincipal user, string permission) =>
            user.HasClaim(c => c.Type == CustomClaimTypes.Permission) && user.HasClaim(CustomClaimTypes.Permission, permission);

        public static bool HasAPermission(this ClaimsPrincipal user, params string[] permissions) =>
            user.HasClaim(c => c.Type == CustomClaimTypes.Permission) && permissions.Any(perm => user.HasClaim(CustomClaimTypes.Permission, perm));

        public static int HomeLocationId(this ClaimsPrincipal user)
        {
            var homeLocationIdString = user.FindFirstValue(CustomClaimTypes.HomeLocationId);
            var parsed = int.TryParse(homeLocationIdString, out var homeLocationId);
            return parsed ? homeLocationId : -5000;
        }

        public static Guid CurrentUserId(this ClaimsPrincipal user)
        {
            var userIdString = user.FindFirstValue(CustomClaimTypes.UserId);
            if (!Guid.TryParse(userIdString, out Guid userId))
                throw new InvalidOperationException("Missing UserId Guid from claims.");
            return userId;
        }

    }
}
