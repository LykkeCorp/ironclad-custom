﻿// Copyright (c) Lykke Corp.
// See the LICENSE file in the project root for more information.

namespace Ironclad.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using IdentityServer4.Extensions;
    using IdentityServer4.Models;
    using IdentityServer4.Postgresql.Mappers;
    using Ironclad.Client;
    using Marten;
    using Microsoft.AspNetCore.Mvc;
    using IdentityServerClient = IdentityServer4.Models.Client;
    using IroncladClient = Ironclad.Client.Client;
    using PostgresClient = IdentityServer4.Postgresql.Entities.Client;

    [Route("api/[controller]")]
    public class ClientsController : Controller
    {
        private readonly IDocumentStore store;

        public ClientsController(IDocumentStore store)
        {
            this.store = store;
        }

        [HttpGet]
        public async Task<IActionResult> Get(int skip = default, int take = 20)
        {
            skip = Math.Max(0, skip);
            take = take < 0 ? 20 : Math.Min(take, 100);

            using (var session = this.store.LightweightSession())
            {
                var totalSize = await session.Query<PostgresClient>().CountAsync();
                var documents = await session.Query<PostgresClient>().Skip(skip).Take(take).ToListAsync();
                var resources = documents.Select(
                    document =>
                    new ClientSummaryResource
                    {
                        Url = this.HttpContext.GetIdentityServerRelativeUrl("~/api/clients/" + document.ClientId),
                        Id = document.ClientId,
                        Name = document.ClientName,
                        Enabled = document.Enabled,
                    });

                var resourceSet = new ResourceSet<ClientSummaryResource>(skip, totalSize, resources);

                return this.Ok(resourceSet);
            }
        }

        [HttpHead("{clientId}")]
        [HttpGet("{clientId}")]
        public async Task<IActionResult> Get(string clientId)
        {
            using (var session = this.store.LightweightSession())
            {
                var document = await session.Query<PostgresClient>().SingleOrDefaultAsync(item => item.ClientId == clientId);
                if (document == null)
                {
                    return this.NotFound(new { Message = $"Client '{clientId}' not found" });
                }

                return this.Ok(
                    new ClientResource
                    {
                        Url = this.HttpContext.GetIdentityServerRelativeUrl("~/api/clients/" + document.ClientId),
                        Id = document.ClientId,
                        Name = document.ClientName,
                        AllowedCorsOrigins = document.AllowedCorsOrigins.Select(item => item.Origin).ToList(),
                        RedirectUris = document.RedirectUris.Select(item => item.RedirectUri).ToList(),
                        PostLogoutRedirectUris =
                            document.PostLogoutRedirectUris.Select(item => item.PostLogoutRedirectUri).ToList(),
                        AllowedScopes = document.AllowedScopes.Select(item => item.Scope).ToList(),
                        AccessTokenType = ((AccessTokenType)document.AccessTokenType).ToString(),
                        Enabled = document.Enabled,
                    });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] IroncladClient model)
        {
            var client = new IdentityServerClient
            {
                ClientId = model.Id,
                ClientSecrets = new List<Secret> { new Secret(model.Secret.Sha256()) },
                ClientName = model.Name,
            };

            using (var session = this.store.LightweightSession())
            {
                if (session.Query<PostgresClient>().Any(document => document.ClientId == client.ClientId))
                {
                    return this.StatusCode((int)HttpStatusCode.Conflict, new { Message = "Client already exists" });
                }

                session.Insert(client.ToEntity());

                await session.SaveChangesAsync();
            }

            this.Response.Headers.Add("Location", this.HttpContext.GetIdentityServerRelativeUrl("~/api/clients/" + client.ClientId));

            return this.Ok();
        }

        [HttpPut("{clientId}")]
        public async Task<IActionResult> Put(string clientId, [FromBody] IroncladClient model)
        {
            using (var session = this.store.LightweightSession())
            {
                var document = await session.Query<PostgresClient>().SingleOrDefaultAsync(item => item.ClientId == clientId);
                if (document == null)
                {
                    return this.NotFound(new { Message = $"Client '{clientId}' not found" });
                }

                // NOTE (Cameron): Because of the mapping/conversion unknowns we rely upon the Postgres integration to perform that operation which is why we do this...
                var client = new IdentityServerClient
                {
                    AllowedCorsOrigins = model.AllowedCorsOrigins,
                    RedirectUris = model.RedirectUris,
                    PostLogoutRedirectUris = model.PostLogoutRedirectUris,
                    AllowedScopes = model.AllowedScopes,
                };

                // NOTE (Cameron): If the secret is updated we want to add the new secret...
                if (!string.IsNullOrEmpty(model.Secret))
                {
                    client.ClientSecrets = new List<Secret> { new Secret(model.Secret.Sha256()) };
                }

                var entity = client.ToEntity();

                int accessTokenType = document.AccessTokenType;
                if (model.AccessTokenType != null)
                {
                    if (!Enum.TryParse<AccessTokenType>(model.AccessTokenType, out var accessTokenTypeEnum))
                    {
                        return this.BadRequest(new { Message = $"Token type [{model.AccessTokenType}] does not exists." });
                    }

                    accessTokenType = (int)accessTokenTypeEnum;
                }

                // update properties (everything supported is an optional update eg. if null is passed we will not update)
                document.ClientName = model.Name ?? document.ClientName;
                document.AllowedCorsOrigins = entity.AllowedCorsOrigins ?? document.AllowedCorsOrigins;
                document.RedirectUris = entity.RedirectUris ?? document.RedirectUris;
                document.PostLogoutRedirectUris = entity.PostLogoutRedirectUris ?? document.PostLogoutRedirectUris;
                document.AllowedScopes = entity.AllowedScopes ?? document.AllowedScopes;
                document.AccessTokenType = (int)accessTokenType;
                document.Enabled = model.Enabled ?? document.Enabled;

                if (!string.IsNullOrEmpty(model.Secret))
                {
                    document.ClientSecrets.Add(entity.ClientSecrets.First());
                }

                session.Update(document);

                await session.SaveChangesAsync();
            }

            return this.Ok();
        }

        [HttpDelete("{clientId}")]
        public async Task<IActionResult> Delete(string clientId)
        {
            using (var session = this.store.LightweightSession())
            {
                session.DeleteWhere<PostgresClient>(document => document.ClientId == clientId);
                await session.SaveChangesAsync();
            }

            return this.Ok();
        }

#pragma warning disable CA1034, CA1056
        public class ClientResource : IroncladClient
        {
            public string Url { get; set; }
        }

        private class ClientSummaryResource : ClientSummary
        {
            public string Url { get; set; }
        }
    }
}