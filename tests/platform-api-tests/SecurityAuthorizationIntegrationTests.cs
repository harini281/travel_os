using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SkiaSharp;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;
using Xunit;

namespace TravelOS.PlatformApi.Tests;

public sealed class SecurityAuthorizationIntegrationTests(TravelOsApiFactory factory)
    : IClassFixture<TravelOsApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    [PostgresFact]
    public async Task RecommendationsAndBudget_AreScopedToCurrentTripMembersAndOwners()
    {
        var seed = await SeedTripAsync();
        using var memberRecommendation = AuthorizedRequest(
            seed.MemberId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/itinerary/recommendations",
            new { dayNumber = 1, focus = "weather", travellerCount = 2 });
        using var memberRecommendationResponse = await Client.SendAsync(memberRecommendation);
        // The member passed authorization; this empty test trip has no itinerary yet.
        Assert.Equal(HttpStatusCode.Conflict, memberRecommendationResponse.StatusCode);

        using var outsiderRecommendation = AuthorizedRequest(
            seed.OutsiderId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/itinerary/recommendations",
            new { dayNumber = 1, focus = "weather", travellerCount = 2 });
        using var outsiderRecommendationResponse = await Client.SendAsync(outsiderRecommendation);
        Assert.Equal(HttpStatusCode.NotFound, outsiderRecommendationResponse.StatusCode);

        using var memberSchedule = AuthorizedRequest(
            seed.MemberId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/itinerary/recommendations/schedule",
            new
            {
                dayNumber = 1, title = "Museum visit", durationMinutes = 60,
                dayStartTime = "09:00", dayEndTime = "18:00", bufferMinutes = 15
            });
        using var memberScheduleResponse = await Client.SendAsync(memberSchedule);
        Assert.Equal(HttpStatusCode.NotFound, memberScheduleResponse.StatusCode);

        using var memberBudget = AuthorizedRequest(
            seed.MemberId, HttpMethod.Get, $"/api/v1/trips/{seed.TripId}/budget");
        using var memberBudgetResponse = await Client.SendAsync(memberBudget);
        Assert.Equal(HttpStatusCode.NoContent, memberBudgetResponse.StatusCode);

        using var outsiderBudget = AuthorizedRequest(
            seed.OutsiderId, HttpMethod.Get, $"/api/v1/trips/{seed.TripId}/budget");
        using var outsiderBudgetResponse = await Client.SendAsync(outsiderBudget);
        Assert.Equal(HttpStatusCode.NotFound, outsiderBudgetResponse.StatusCode);

        using var memberBudgetWrite = AuthorizedRequest(
            seed.MemberId, HttpMethod.Put, $"/api/v1/trips/{seed.TripId}/budget",
            new { amount = 500m, currencyCode = "LKR" });
        using var memberBudgetWriteResponse = await Client.SendAsync(memberBudgetWrite);
        Assert.Equal(HttpStatusCode.NotFound, memberBudgetWriteResponse.StatusCode);
    }

    [PostgresFact]
    public async Task Expenses_RestrictEditsToCreatorOrOwnerAndHideRecordsFromOutsiders()
    {
        var seed = await SeedTripAsync();
        var expenseId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
            db.TripExpenses.Add(new TripExpense
            {
                Id = expenseId,
                TripId = seed.TripId,
                PaidByUserId = seed.OwnerId,
                CreatedByUserId = seed.OwnerId,
                Title = "Owner expense",
                Category = "food",
                Amount = 100m,
                CurrencyCode = "LKR",
                ExpenseDate = new DateOnly(2026, 12, 1),
                Shares =
                [
                    new TripExpenseShare
                    {
                        ExpenseId = expenseId,
                        UserId = seed.MemberId,
                        ShareAmount = 100m
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        using var memberHistory = AuthorizedRequest(
            seed.MemberId, HttpMethod.Get,
            $"/api/v1/trips/{seed.TripId}/expenses/{expenseId}/history");
        using var memberHistoryResponse = await Client.SendAsync(memberHistory);
        Assert.Equal(HttpStatusCode.OK, memberHistoryResponse.StatusCode);

        using var outsiderHistory = AuthorizedRequest(
            seed.OutsiderId, HttpMethod.Get,
            $"/api/v1/trips/{seed.TripId}/expenses/{expenseId}/history");
        using var outsiderHistoryResponse = await Client.SendAsync(outsiderHistory);
        Assert.Equal(HttpStatusCode.NotFound, outsiderHistoryResponse.StatusCode);

        using var memberEdit = AuthorizedRequest(
            seed.MemberId, HttpMethod.Put,
            $"/api/v1/trips/{seed.TripId}/expenses/{expenseId}",
            new
            {
                title = "Unauthorized member edit",
                category = "food",
                amount = 999m,
                currencyCode = "LKR",
                expenseDate = "2026-12-01",
                paidByUserId = seed.OwnerId,
                participantUserIds = new[] { seed.OwnerId, seed.MemberId },
                splitType = "equal"
            });
        using var memberEditResponse = await Client.SendAsync(memberEdit);
        Assert.Equal(HttpStatusCode.Forbidden, memberEditResponse.StatusCode);

        using var outsiderEdit = AuthorizedRequest(
            seed.OutsiderId, HttpMethod.Put,
            $"/api/v1/trips/{seed.TripId}/expenses/{expenseId}",
            new { title = "Outsider edit" });
        using var outsiderEditResponse = await Client.SendAsync(outsiderEdit);
        Assert.Equal(HttpStatusCode.NotFound, outsiderEditResponse.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        Assert.Equal("Owner expense", await verifyDb.TripExpenses
            .Where(x => x.Id == expenseId).Select(x => x.Title).SingleAsync());
    }

    [PostgresFact]
    public async Task Repayments_CanOnlyBeRespondedToByTheRecipient()
    {
        var seed = await SeedTripAsync();
        var repaymentId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
            db.TripRepayments.Add(new TripRepayment
            {
                Id = repaymentId,
                TripId = seed.TripId,
                FromUserId = seed.MemberId,
                ToUserId = seed.OwnerId,
                CreatedByUserId = seed.MemberId,
                Amount = 50m,
                CurrencyCode = "LKR",
                Status = "pending"
            });
            await db.SaveChangesAsync();
        }

        using var senderResponseRequest = AuthorizedRequest(
            seed.MemberId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/repayments/{repaymentId}/respond",
            new { decision = "reject" });
        using var senderResponse = await Client.SendAsync(senderResponseRequest);
        Assert.Equal(HttpStatusCode.Forbidden, senderResponse.StatusCode);

        using var outsiderResponseRequest = AuthorizedRequest(
            seed.OutsiderId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/repayments/{repaymentId}/respond",
            new { decision = "reject" });
        using var outsiderResponse = await Client.SendAsync(outsiderResponseRequest);
        Assert.Equal(HttpStatusCode.NotFound, outsiderResponse.StatusCode);

        using var recipientResponseRequest = AuthorizedRequest(
            seed.OwnerId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/repayments/{repaymentId}/respond",
            new { decision = "reject" });
        using var recipientResponse = await Client.SendAsync(recipientResponseRequest);
        Assert.Equal(HttpStatusCode.OK, recipientResponse.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        Assert.Equal("rejected", await verifyDb.TripRepayments
            .Where(x => x.Id == repaymentId).Select(x => x.Status).SingleAsync());
    }

    [PostgresFact]
    public async Task Invitations_AreOwnerManagedAndOnlyTheInvitedAccountCanAccept()
    {
        var seed = await SeedTripAsync();
        var invitationId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
            db.TripInvitations.Add(new TripInvitation
            {
                Id = invitationId,
                TripId = seed.TripId,
                InvitedUserId = seed.InvitedId,
                InvitedByUserId = seed.OwnerId,
                Status = "pending",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(2)
            });
            await db.SaveChangesAsync();
        }

        using var ownerList = AuthorizedRequest(
            seed.OwnerId, HttpMethod.Get, $"/api/v1/trips/{seed.TripId}/invitations");
        using var ownerListResponse = await Client.SendAsync(ownerList);
        Assert.Equal(HttpStatusCode.OK, ownerListResponse.StatusCode);

        using var memberList = AuthorizedRequest(
            seed.MemberId, HttpMethod.Get, $"/api/v1/trips/{seed.TripId}/invitations");
        using var memberListResponse = await Client.SendAsync(memberList);
        Assert.Equal(HttpStatusCode.NotFound, memberListResponse.StatusCode);

        using var memberAccept = AuthorizedRequest(
            seed.MemberId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/invitations/{invitationId}/accept");
        using var memberAcceptResponse = await Client.SendAsync(memberAccept);
        Assert.Equal(HttpStatusCode.NotFound, memberAcceptResponse.StatusCode);

        await using (var verifyScope = factory.Services.CreateAsyncScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
            Assert.Equal("pending", await verifyDb.TripInvitations
                .Where(x => x.Id == invitationId).Select(x => x.Status).SingleAsync());
        }

        using var inviteeAccept = AuthorizedRequest(
            seed.InvitedId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/invitations/{invitationId}/accept");
        using var inviteeAcceptResponse = await Client.SendAsync(inviteeAccept);
        Assert.Equal(HttpStatusCode.OK, inviteeAcceptResponse.StatusCode);

        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        Assert.Equal("accepted", await finalDb.TripInvitations
            .Where(x => x.Id == invitationId).Select(x => x.Status).SingleAsync());
        Assert.True(await finalDb.TripMembers.AnyAsync(
            x => x.TripId == seed.TripId && x.UserId == seed.InvitedId));
    }

    [PostgresFact]
    public async Task ReceiptFile_IsVisibleToTripMembersButMutationIsUploaderOrOwnerOnly()
    {
        var seed = await SeedTripAsync();
        var receiptId = Guid.NewGuid();
        var storedFileName = receiptId.ToString("N") + ".jpg";
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        Directory.CreateDirectory(TravelOsApiFactory.ReceiptStoragePath);
        var filePath = Path.Combine(TravelOsApiFactory.ReceiptStoragePath, storedFileName);
        await File.WriteAllBytesAsync(filePath, bytes);

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
                db.TripReceipts.Add(new TripReceipt
                {
                    Id = receiptId,
                    TripId = seed.TripId,
                    UploadedByUserId = seed.OwnerId,
                    OriginalFileName = "expense.jpg",
                    StoredFileName = storedFileName,
                    ContentType = "image/jpeg",
                    FileSizeBytes = bytes.Length,
                    Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                    ExtractionStatus = "pending_review"
                });
                await db.SaveChangesAsync();
            }

            using var memberFile = AuthorizedRequest(
                seed.MemberId, HttpMethod.Get,
                $"/api/v1/trips/{seed.TripId}/receipts/{receiptId}/file");
            using var memberFileResponse = await Client.SendAsync(memberFile);
            Assert.Equal(HttpStatusCode.OK, memberFileResponse.StatusCode);
            Assert.Equal(bytes, await memberFileResponse.Content.ReadAsByteArrayAsync());

            using var outsiderFile = AuthorizedRequest(
                seed.OutsiderId, HttpMethod.Get,
                $"/api/v1/trips/{seed.TripId}/receipts/{receiptId}/file");
            using var outsiderFileResponse = await Client.SendAsync(outsiderFile);
            Assert.Equal(HttpStatusCode.NotFound, outsiderFileResponse.StatusCode);

            using var memberConfirm = AuthorizedRequest(
                seed.MemberId, HttpMethod.Post,
                $"/api/v1/trips/{seed.TripId}/receipts/{receiptId}/confirm",
                new
                {
                    merchantName = "Unauthorized member confirmation",
                    receiptDate = "2026-12-02",
                    totalAmount = 25m,
                    currencyCode = "LKR"
                });
            using var memberConfirmResponse = await Client.SendAsync(memberConfirm);
            Assert.Equal(HttpStatusCode.Forbidden, memberConfirmResponse.StatusCode);

            await using (var confirmVerifyScope = factory.Services.CreateAsyncScope())
            {
                var confirmVerifyDb = confirmVerifyScope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
                var unchangedReceipt = await confirmVerifyDb.TripReceipts.SingleAsync(x => x.Id == receiptId);
                Assert.Null(unchangedReceipt.MerchantName);
                Assert.Null(unchangedReceipt.ConfirmedByUserId);
                Assert.Equal("pending_review", unchangedReceipt.ExtractionStatus);
            }

            using var ownerConfirm = AuthorizedRequest(
                seed.OwnerId, HttpMethod.Post,
                $"/api/v1/trips/{seed.TripId}/receipts/{receiptId}/confirm",
                new
                {
                    merchantName = "Owner confirmed receipt",
                    receiptDate = "2026-12-02",
                    totalAmount = 25m,
                    currencyCode = "LKR"
                });
            using var ownerConfirmResponse = await Client.SendAsync(ownerConfirm);
            Assert.Equal(HttpStatusCode.OK, ownerConfirmResponse.StatusCode);

            using var memberDelete = AuthorizedRequest(
                seed.MemberId, HttpMethod.Delete,
                $"/api/v1/trips/{seed.TripId}/receipts/{receiptId}");
            using var memberDeleteResponse = await Client.SendAsync(memberDelete);
            Assert.Equal(HttpStatusCode.Forbidden, memberDeleteResponse.StatusCode);

            using var ownerDelete = AuthorizedRequest(
                seed.OwnerId, HttpMethod.Delete,
                $"/api/v1/trips/{seed.TripId}/receipts/{receiptId}");
            using var ownerDeleteResponse = await Client.SendAsync(ownerDelete);
            Assert.Equal(HttpStatusCode.Accepted, ownerDeleteResponse.StatusCode);

            using var pendingDownload = AuthorizedRequest(
                seed.MemberId, HttpMethod.Get,
                $"/api/v1/trips/{seed.TripId}/receipts/{receiptId}/file");
            using var pendingDownloadResponse = await Client.SendAsync(pendingDownload);
            Assert.Equal(HttpStatusCode.NotFound, pendingDownloadResponse.StatusCode);

            await WaitForReceiptCleanupAsync(receiptId, filePath);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [PostgresFact]
    public async Task ReceiptUpload_RejectsMimeMismatchAndSanitizesClientFileName()
    {
        var seed = await SeedTripAsync();
        var png = CreatePng(32, 24);
        var uploadPath = $"/api/v1/trips/{seed.TripId}/receipts";

        using var validForm = new MultipartFormDataContent();
        var validFile = new ByteArrayContent(png);
        validFile.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        validForm.Add(validFile, "file", "../../sensitive-receipt.png");
        using var validRequest = AuthorizedRequest(seed.MemberId, HttpMethod.Post, uploadPath);
        validRequest.Content = validForm;

        using var validResponse = await Client.SendAsync(validRequest);
        Assert.Equal(HttpStatusCode.Created, validResponse.StatusCode);
        using var result = System.Text.Json.JsonDocument.Parse(
            await validResponse.Content.ReadAsStringAsync());
        var receiptId = result.RootElement.GetProperty("id").GetGuid();
        var originalName = result.RootElement.GetProperty("originalFileName").GetString();
        var contentType = result.RootElement.GetProperty("contentType").GetString();

        Assert.Equal("sensitive-receipt.png", originalName);
        Assert.Equal("image/jpeg", contentType);

        // Storage names are generated by the API and are intentionally not returned.
        var storedPath = Path.Combine(
            TravelOsApiFactory.ReceiptStoragePath, receiptId.ToString("N") + ".jpg");
        Assert.True(File.Exists(storedPath));
        using (var storedData = SKData.CreateCopy(await File.ReadAllBytesAsync(storedPath)))
        using (var storedCodec = SKCodec.Create(storedData))
            Assert.Equal(SKEncodedImageFormat.Jpeg, storedCodec!.EncodedFormat);

        using var mismatchForm = new MultipartFormDataContent();
        var mismatchFile = new ByteArrayContent(png);
        mismatchFile.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        mismatchForm.Add(mismatchFile, "file", "receipt.jpg");
        using var mismatchRequest = AuthorizedRequest(seed.MemberId, HttpMethod.Post, uploadPath);
        mismatchRequest.Content = mismatchForm;

        using var mismatchResponse = await Client.SendAsync(mismatchRequest);
        Assert.Equal(HttpStatusCode.BadRequest, mismatchResponse.StatusCode);

        await using (var verifyScope = factory.Services.CreateAsyncScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
            Assert.Equal(1, await verifyDb.TripReceipts
                .CountAsync(x => x.TripId == seed.TripId));
        }

        using var deleteRequest = AuthorizedRequest(
            seed.MemberId, HttpMethod.Delete,
            $"/api/v1/trips/{seed.TripId}/receipts/{receiptId}");
        using var deleteResponse = await Client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.Accepted, deleteResponse.StatusCode);
        await WaitForReceiptCleanupAsync(receiptId, storedPath);
    }

    [PostgresFact]
    public async Task ReceiptUpload_RejectsAFileLargerThanEightMegabytes()
    {
        var seed = await SeedTripAsync();
        using var form = new MultipartFormDataContent();
        var oversizedFile = new ByteArrayContent(new byte[8 * 1024 * 1024 + 1]);
        oversizedFile.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(oversizedFile, "file", "oversized.jpg");
        using var request = AuthorizedRequest(
            seed.MemberId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/receipts");
        request.Content = form;

        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        Assert.Equal(0, await db.TripReceipts.CountAsync(x => x.TripId == seed.TripId));
    }

    [PostgresFact]
    public async Task PreferencesAndGroupPolls_AreScopedToCurrentTripMembers()
    {
        var seed = await SeedTripAsync();

        using var memberSavePreference = AuthorizedRequest(
            seed.MemberId, HttpMethod.Put,
            $"/api/v1/trips/{seed.TripId}/preferences/me",
            new
            {
                interests = new[] { "culture", "food" },
                pace = "balanced",
                travelStyle = "mixed",
                dailyBudgetAmount = 125m,
                dailyBudgetCurrencyCode = "LKR",
                notes = "Member's private preference"
            });
        using var memberSavePreferenceResponse = await Client.SendAsync(memberSavePreference);
        Assert.Equal(HttpStatusCode.OK, memberSavePreferenceResponse.StatusCode);

        using var memberGetPreference = AuthorizedRequest(
            seed.MemberId, HttpMethod.Get,
            $"/api/v1/trips/{seed.TripId}/preferences/me");
        using var memberGetPreferenceResponse = await Client.SendAsync(memberGetPreference);
        Assert.Equal(HttpStatusCode.OK, memberGetPreferenceResponse.StatusCode);
        using (var preferenceJson = System.Text.Json.JsonDocument.Parse(
            await memberGetPreferenceResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(seed.MemberId,
                preferenceJson.RootElement.GetProperty("userId").GetGuid());
        }

        using var ownerGetGroupPreferences = AuthorizedRequest(
            seed.OwnerId, HttpMethod.Get,
            $"/api/v1/trips/{seed.TripId}/preferences/group");
        using var ownerGetGroupPreferencesResponse = await Client.SendAsync(ownerGetGroupPreferences);
        Assert.Equal(HttpStatusCode.OK, ownerGetGroupPreferencesResponse.StatusCode);

        using var outsiderGetGroupPreferences = AuthorizedRequest(
            seed.OutsiderId, HttpMethod.Get,
            $"/api/v1/trips/{seed.TripId}/preferences/group");
        using var outsiderGetGroupPreferencesResponse = await Client.SendAsync(outsiderGetGroupPreferences);
        Assert.Equal(HttpStatusCode.NotFound, outsiderGetGroupPreferencesResponse.StatusCode);

        using var outsiderSavePreference = AuthorizedRequest(
            seed.OutsiderId, HttpMethod.Put,
            $"/api/v1/trips/{seed.TripId}/preferences/me",
            new
            {
                interests = new[] { "shopping" },
                pace = "fast",
                travelStyle = "culture",
                dailyBudgetAmount = 10m,
                dailyBudgetCurrencyCode = "LKR",
                notes = "Unauthorized preference"
            });
        using var outsiderSavePreferenceResponse = await Client.SendAsync(outsiderSavePreference);
        Assert.Equal(HttpStatusCode.NotFound, outsiderSavePreferenceResponse.StatusCode);

        using var ownerCreatePoll = AuthorizedRequest(
            seed.OwnerId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/polls",
            new
            {
                title = "Choose a group activity",
                description = "Security test poll",
                options = new[] { "Museum", "Beach" }
            });
        using var ownerCreatePollResponse = await Client.SendAsync(ownerCreatePoll);
        Assert.Equal(HttpStatusCode.Created, ownerCreatePollResponse.StatusCode);
        using var pollJson = System.Text.Json.JsonDocument.Parse(
            await ownerCreatePollResponse.Content.ReadAsStringAsync());
        var pollId = pollJson.RootElement.GetProperty("id").GetGuid();

        using var memberListPolls = AuthorizedRequest(
            seed.MemberId, HttpMethod.Get, $"/api/v1/trips/{seed.TripId}/polls");
        using var memberListPollsResponse = await Client.SendAsync(memberListPolls);
        Assert.Equal(HttpStatusCode.OK, memberListPollsResponse.StatusCode);

        using var outsiderListPolls = AuthorizedRequest(
            seed.OutsiderId, HttpMethod.Get, $"/api/v1/trips/{seed.TripId}/polls");
        using var outsiderListPollsResponse = await Client.SendAsync(outsiderListPolls);
        Assert.Equal(HttpStatusCode.NotFound, outsiderListPollsResponse.StatusCode);

        var optionId = pollJson.RootElement.TryGetProperty("options", out var createdOptions)
            ? createdOptions[0].GetProperty("id").GetGuid()
            : Guid.Empty;
        if (optionId == Guid.Empty)
        {
            using var listJson = System.Text.Json.JsonDocument.Parse(
                await memberListPollsResponse.Content.ReadAsStringAsync());
            optionId = listJson.RootElement[0].GetProperty("options")[0].GetProperty("id").GetGuid();
        }

        using var outsiderVote = AuthorizedRequest(
            seed.OutsiderId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/polls/{pollId}/vote",
            new { optionId });
        using var outsiderVoteResponse = await Client.SendAsync(outsiderVote);
        Assert.Equal(HttpStatusCode.NotFound, outsiderVoteResponse.StatusCode);

        using var memberVote = AuthorizedRequest(
            seed.MemberId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/polls/{pollId}/vote",
            new { optionId });
        using var memberVoteResponse = await Client.SendAsync(memberVote);
        Assert.Equal(HttpStatusCode.OK, memberVoteResponse.StatusCode);

        using var memberClosePoll = AuthorizedRequest(
            seed.MemberId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/polls/{pollId}/close");
        using var memberClosePollResponse = await Client.SendAsync(memberClosePoll);
        Assert.Equal(HttpStatusCode.Forbidden, memberClosePollResponse.StatusCode);

        await using (var verifyScope = factory.Services.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
            Assert.True(await db.GroupPollVotes.AnyAsync(
                x => x.PollId == pollId && x.UserId == seed.MemberId && x.OptionId == optionId));
            Assert.Equal("open", await db.GroupPolls
                .Where(x => x.Id == pollId).Select(x => x.Status).SingleAsync());
        }

        using var ownerClosePoll = AuthorizedRequest(
            seed.OwnerId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/polls/{pollId}/close");
        using var ownerClosePollResponse = await Client.SendAsync(ownerClosePoll);
        Assert.Equal(HttpStatusCode.OK, ownerClosePollResponse.StatusCode);

        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        Assert.Equal("closed", await finalDb.GroupPolls
            .Where(x => x.Id == pollId).Select(x => x.Status).SingleAsync());
    }

    [PostgresFact]
    public async Task ReceiptUpload_RejectsOutsiderWithoutPersistingFileOrRecord()
    {
        var seed = await SeedTripAsync();
        var storageRoot = TravelOsApiFactory.ReceiptStoragePath;
        Directory.CreateDirectory(storageRoot);
        var filesBefore = Directory.GetFiles(storageRoot).ToHashSet(StringComparer.Ordinal);
        var png = CreatePng(16, 16);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "outsider-receipt.png");
        using var request = AuthorizedRequest(
            seed.OutsiderId, HttpMethod.Post,
            $"/api/v1/trips/{seed.TripId}/receipts");
        request.Content = form;

        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        Assert.Equal(0, await db.TripReceipts.CountAsync(x => x.TripId == seed.TripId));
        Assert.Equal(filesBefore, Directory.GetFiles(storageRoot).ToHashSet(StringComparer.Ordinal));
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 95);
        Assert.NotNull(encoded);
        return encoded.ToArray();
    }

    private async Task WaitForReceiptCleanupAsync(Guid receiptId, string filePath)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
            var exists = await db.TripReceipts.AnyAsync(x => x.Id == receiptId);
            if (!exists && !File.Exists(filePath)) return;
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.False(File.Exists(filePath), "Receipt worker did not remove the queued file within the test timeout.");
        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        Assert.False(await finalDb.TripReceipts.AnyAsync(x => x.Id == receiptId),
            "Receipt worker did not remove the queued metadata within the test timeout.");
    }

    private async Task<SeededTrip> SeedTripAsync()
    {
        var seed = new SeededTrip(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        db.Users.AddRange(
            NewUser(seed.OwnerId), NewUser(seed.MemberId), NewUser(seed.OutsiderId),
            NewUser(seed.InvitedId));
        db.Trips.Add(new Trip
        {
            Id = seed.TripId,
            OwnerUserId = seed.OwnerId,
            Name = "Authorization regression trip",
            Destination = "Colombo",
            StartDate = new DateOnly(2026, 12, 1),
            EndDate = new DateOnly(2026, 12, 3),
            CurrencyCode = "LKR",
            Members =
            [
                new TripMember { TripId = seed.TripId, UserId = seed.OwnerId, Role = "owner" },
                new TripMember { TripId = seed.TripId, UserId = seed.MemberId, Role = "member" }
            ]
        });
        await db.SaveChangesAsync();
        return seed;
    }

    private static User NewUser(Guid id)
    {
        var email = $"security-{id:N}@example.test";
        return new User
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            DisplayName = "Security test user",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static HttpRequestMessage AuthorizedRequest(
        Guid userId, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = System.Net.Http.Json.JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId));
        return request;
    }

    private static string CreateToken(Guid userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TravelOsApiFactory.TestSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TravelOsApiFactory.TestIssuer,
            audience: TravelOsApiFactory.TestAudience,
            claims: [new Claim("sub", userId.ToString())],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record SeededTrip(
        Guid OwnerId, Guid MemberId, Guid OutsiderId, Guid InvitedId, Guid TripId);
}
