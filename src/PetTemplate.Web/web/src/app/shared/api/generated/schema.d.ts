/**
 * Сгенерировано `npm run api:types` (tools/api-types.mjs) из OpenAPI документа PetTemplate.Api.
 * Руками не править: правка DTO на сервере — и перегенерировать.
 */

export interface paths {
    "/api/Account/Logins": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AccountLoginsDto"];
                        "application/json": components["schemas"]["AccountLoginsDto"];
                        "text/json": components["schemas"]["AccountLoginsDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/Link": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["ExternalProofDto"];
                    "text/json": components["schemas"]["ExternalProofDto"];
                    "application/*+json": components["schemas"]["ExternalProofDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["LinkResultDto"];
                        "application/json": components["schemas"]["LinkResultDto"];
                        "text/json": components["schemas"]["LinkResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/Unlink": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["UnlinkDto"];
                    "text/json": components["schemas"]["UnlinkDto"];
                    "application/*+json": components["schemas"]["UnlinkDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AccountLoginsDto"];
                        "application/json": components["schemas"]["AccountLoginsDto"];
                        "text/json": components["schemas"]["AccountLoginsDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/Save": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AccountSaveDto"];
                    "text/json": components["schemas"]["AccountSaveDto"];
                    "application/*+json": components["schemas"]["AccountSaveDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AccountLoginsDto"];
                        "application/json": components["schemas"]["AccountLoginsDto"];
                        "text/json": components["schemas"]["AccountLoginsDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/SetLanguage": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["SetLanguageDto"];
                    "text/json": components["schemas"]["SetLanguageDto"];
                    "application/*+json": components["schemas"]["SetLanguageDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/SendEmailConfirmation": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/ChangeUserName": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["ChangeUserNameDto"];
                    "text/json": components["schemas"]["ChangeUserNameDto"];
                    "application/*+json": components["schemas"]["ChangeUserNameDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/ConfirmPassword": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["ConfirmPasswordDto"];
                    "text/json": components["schemas"]["ConfirmPasswordDto"];
                    "application/*+json": components["schemas"]["ConfirmPasswordDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/SetPassword": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["SetPasswordDto"];
                    "text/json": components["schemas"]["SetPasswordDto"];
                    "application/*+json": components["schemas"]["SetPasswordDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/ChangePassword": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["ChangePasswordDto"];
                    "text/json": components["schemas"]["ChangePasswordDto"];
                    "application/*+json": components["schemas"]["ChangePasswordDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/MergePreview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["MergeProofDto"];
                    "text/json": components["schemas"]["MergeProofDto"];
                    "application/*+json": components["schemas"]["MergeProofDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["MergePreviewDto"];
                        "application/json": components["schemas"]["MergePreviewDto"];
                        "text/json": components["schemas"]["MergePreviewDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/Merge": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["MergeDto"];
                    "text/json": components["schemas"]["MergeDto"];
                    "application/*+json": components["schemas"]["MergeDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/DeletePreview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AccountCardDto"];
                        "application/json": components["schemas"]["AccountCardDto"];
                        "text/json": components["schemas"]["AccountCardDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Account/Delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["DeleteAccountDto"];
                    "text/json": components["schemas"]["DeleteAccountDto"];
                    "application/*+json": components["schemas"]["DeleteAccountDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/AccessLog": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    AdminUserId?: string;
                    From?: string;
                    To?: string;
                    Before?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminAccessLogPageDto"];
                        "application/json": components["schemas"]["AdminAccessLogPageDto"];
                        "text/json": components["schemas"]["AdminAccessLogPageDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Accounts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    query?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminAccountDto"][];
                        "application/json": components["schemas"]["AdminAccountDto"][];
                        "text/json": components["schemas"]["AdminAccountDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Accounts/Journal": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    Query?: string;
                    Before?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminAccountJournalPageDto"];
                        "application/json": components["schemas"]["AdminAccountJournalPageDto"];
                        "text/json": components["schemas"]["AdminAccountJournalPageDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Accounts/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminAccountCardDto"];
                        "application/json": components["schemas"]["AdminAccountCardDto"];
                        "text/json": components["schemas"]["AdminAccountCardDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Accounts/{id}/Delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AdminAccountReasonDto"];
                    "text/json": components["schemas"]["AdminAccountReasonDto"];
                    "application/*+json": components["schemas"]["AdminAccountReasonDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Accounts/{id}/IssueReset": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AdminAccountReasonDto"];
                    "text/json": components["schemas"]["AdminAccountReasonDto"];
                    "application/*+json": components["schemas"]["AdminAccountReasonDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminIssuedResetDto"];
                        "application/json": components["schemas"]["AdminIssuedResetDto"];
                        "text/json": components["schemas"]["AdminIssuedResetDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Accounts/{id}/ReleaseEmail": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AdminReleaseEmailDto"];
                    "text/json": components["schemas"]["AdminReleaseEmailDto"];
                    "application/*+json": components["schemas"]["AdminReleaseEmailDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminReleasedContactDto"];
                        "application/json": components["schemas"]["AdminReleasedContactDto"];
                        "text/json": components["schemas"]["AdminReleasedContactDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Accounts/{id}/ReleasePhone": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AdminReleasePhoneDto"];
                    "text/json": components["schemas"]["AdminReleasePhoneDto"];
                    "application/*+json": components["schemas"]["AdminReleasePhoneDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminReleasedContactDto"];
                        "application/json": components["schemas"]["AdminReleasedContactDto"];
                        "text/json": components["schemas"]["AdminReleasedContactDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Admins": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminMemberDto"][];
                        "application/json": components["schemas"]["AdminMemberDto"][];
                        "text/json": components["schemas"]["AdminMemberDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Admins/{userId}/Roles/{role}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    userId: string;
                    role: components["schemas"]["AdminRole"];
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    userId: string;
                    role: components["schemas"]["AdminRole"];
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Admins/{userId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    userId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Login": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["LoginDto"];
                    "text/json": components["schemas"]["LoginDto"];
                    "application/*+json": components["schemas"]["LoginDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminLoginResultDto"];
                        "application/json": components["schemas"]["AdminLoginResultDto"];
                        "text/json": components["schemas"]["AdminLoginResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Invitations/{token}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminInvitationPreviewDto"];
                        "application/json": components["schemas"]["AdminInvitationPreviewDto"];
                        "text/json": components["schemas"]["AdminInvitationPreviewDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Invitations/{token}/Accept": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["LoginDto"];
                    "text/json": components["schemas"]["LoginDto"];
                    "application/*+json": components["schemas"]["LoginDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminLoginResultDto"];
                        "application/json": components["schemas"]["AdminLoginResultDto"];
                        "text/json": components["schemas"]["AdminLoginResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Me": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminDto"];
                        "application/json": components["schemas"]["AdminDto"];
                        "text/json": components["schemas"]["AdminDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Logout": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Auth/Verify": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Billing/Summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    from?: string;
                    to?: string;
                    modelId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminBillingSummaryDto"];
                        "application/json": components["schemas"]["AdminBillingSummaryDto"];
                        "text/json": components["schemas"]["AdminBillingSummaryDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Billing/Ledger": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AdminLedgerEntryDto"];
                    "text/json": components["schemas"]["AdminLedgerEntryDto"];
                    "application/*+json": components["schemas"]["AdminLedgerEntryDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": string;
                        "application/json": string;
                        "text/json": string;
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Billing/Workspaces/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    from?: string;
                    to?: string;
                };
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminBillingWorkspaceCardDto"];
                        "application/json": components["schemas"]["AdminBillingWorkspaceCardDto"];
                        "text/json": components["schemas"]["AdminBillingWorkspaceCardDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Billing/Workspaces/{id}/Limit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AdminAssistantLimitDto"];
                    "text/json": components["schemas"]["AdminAssistantLimitDto"];
                    "application/*+json": components["schemas"]["AdminAssistantLimitDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Dialogs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    From?: string;
                    To?: string;
                    Workspace?: string;
                    ModelId?: string;
                    Status?: components["schemas"]["AssistantRunStatus"];
                    MinClarifications?: number;
                    MaxSatisfaction?: number;
                    Before?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminDialogPageDto"];
                        "application/json": components["schemas"]["AdminDialogPageDto"];
                        "text/json": components["schemas"]["AdminDialogPageDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Dialogs/Evaluations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminEvaluationsDto"];
                        "application/json": components["schemas"]["AdminEvaluationsDto"];
                        "text/json": components["schemas"]["AdminEvaluationsDto"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AdminEvaluationStartDto"];
                    "text/json": components["schemas"]["AdminEvaluationStartDto"];
                    "application/*+json": components["schemas"]["AdminEvaluationStartDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminJobDto"];
                        "application/json": components["schemas"]["AdminJobDto"];
                        "text/json": components["schemas"]["AdminJobDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Dialogs/{runId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    runId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminDialogTraceDto"];
                        "application/json": components["schemas"]["AdminDialogTraceDto"];
                        "text/json": components["schemas"]["AdminDialogTraceDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Invitations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminInvitationDto"][];
                        "application/json": components["schemas"]["AdminInvitationDto"][];
                        "text/json": components["schemas"]["AdminInvitationDto"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AdminInvitationCreateDto"];
                    "text/json": components["schemas"]["AdminInvitationCreateDto"];
                    "application/*+json": components["schemas"]["AdminInvitationCreateDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminInvitationIssuedDto"];
                        "application/json": components["schemas"]["AdminInvitationIssuedDto"];
                        "text/json": components["schemas"]["AdminInvitationIssuedDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Invitations/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Models": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminModelsDto"];
                        "application/json": components["schemas"]["AdminModelsDto"];
                        "text/json": components["schemas"]["AdminModelsDto"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AdminModelEditDto"];
                    "text/json": components["schemas"]["AdminModelEditDto"];
                    "application/*+json": components["schemas"]["AdminModelEditDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": string;
                        "application/json": string;
                        "text/json": string;
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Models/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AdminModelEditDto"];
                    "text/json": components["schemas"]["AdminModelEditDto"];
                    "application/*+json": components["schemas"]["AdminModelEditDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Models/{id}/Close": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Models/{id}/Check": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminJobDto"];
                        "application/json": components["schemas"]["AdminJobDto"];
                        "text/json": components["schemas"]["AdminJobDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Models/Checks": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminJobDto"][];
                        "application/json": components["schemas"]["AdminJobDto"][];
                        "text/json": components["schemas"]["AdminJobDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Models/Checks/{jobId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    jobId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminJobDto"];
                        "application/json": components["schemas"]["AdminJobDto"];
                        "text/json": components["schemas"]["AdminJobDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Models/Order": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": string[];
                    "text/json": string[];
                    "application/*+json": string[];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Search/Reindex": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Admin/Summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AdminSummaryDto"];
                        "application/json": components["schemas"]["AdminSummaryDto"];
                        "text/json": components["schemas"]["AdminSummaryDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Assistant/History": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    cursor?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AssistantHistoryDto"];
                        "application/json": components["schemas"]["AssistantHistoryDto"];
                        "text/json": components["schemas"]["AssistantHistoryDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Assistant/Send": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["AssistantSendDto"];
                    "text/json": components["schemas"]["AssistantSendDto"];
                    "application/*+json": components["schemas"]["AssistantSendDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AssistantSendResultDto"];
                        "application/json": components["schemas"]["AssistantSendResultDto"];
                        "text/json": components["schemas"]["AssistantSendResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Assistant/Run/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    wait?: number;
                };
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AssistantRunDto"];
                        "application/json": components["schemas"]["AssistantRunDto"];
                        "text/json": components["schemas"]["AssistantRunDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Assistant/Run/{id}/Stop": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Assistant/Run/{id}/Trace": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AssistantTraceDto"];
                        "application/json": components["schemas"]["AssistantTraceDto"];
                        "text/json": components["schemas"]["AssistantTraceDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Assistant/Clear": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Assistant/Transcribe": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "multipart/form-data": {
                        /** Format: binary */
                        audio?: string;
                        /** Format: int32 */
                        seconds?: number;
                    };
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AssistantTranscriptionDto"];
                        "application/json": components["schemas"]["AssistantTranscriptionDto"];
                        "text/json": components["schemas"]["AssistantTranscriptionDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Assistant/Memory": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AssistantMemoryDto"];
                        "application/json": components["schemas"]["AssistantMemoryDto"];
                        "text/json": components["schemas"]["AssistantMemoryDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Assistant/Memory/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/ExternalLogins": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["ExternalLoginsConfigDto"];
                        "application/json": components["schemas"]["ExternalLoginsConfigDto"];
                        "text/json": components["schemas"]["ExternalLoginsConfigDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/TelegramLogin": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["ExternalCodeDto"];
                    "text/json": components["schemas"]["ExternalCodeDto"];
                    "application/*+json": components["schemas"]["ExternalCodeDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/VkId": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["ExternalCodeDto"];
                    "text/json": components["schemas"]["ExternalCodeDto"];
                    "application/*+json": components["schemas"]["ExternalCodeDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/TelegramAuth": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["TelegramAuthDto"];
                    "text/json": components["schemas"]["TelegramAuthDto"];
                    "application/*+json": components["schemas"]["TelegramAuthDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/VkAuth": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["VkAuthDto"];
                    "text/json": components["schemas"]["VkAuthDto"];
                    "application/*+json": components["schemas"]["VkAuthDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/RegisterExternal": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["RegisterExternalDto"];
                    "text/json": components["schemas"]["RegisterExternalDto"];
                    "application/*+json": components["schemas"]["RegisterExternalDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/LogInAndLink": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["LogInAndLinkDto"];
                    "text/json": components["schemas"]["LogInAndLinkDto"];
                    "application/*+json": components["schemas"]["LogInAndLinkDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/ConfirmEmail": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["EmailTokenDto"];
                    "text/json": components["schemas"]["EmailTokenDto"];
                    "application/*+json": components["schemas"]["EmailTokenDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/RequestReset": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["RequestResetDto"];
                    "text/json": components["schemas"]["RequestResetDto"];
                    "application/*+json": components["schemas"]["RequestResetDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/Reset": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["ResetPasswordDto"];
                    "text/json": components["schemas"]["ResetPasswordDto"];
                    "application/*+json": components["schemas"]["ResetPasswordDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/Register": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["RegisterDto"];
                    "text/json": components["schemas"]["RegisterDto"];
                    "application/*+json": components["schemas"]["RegisterDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Auth/LogIn": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["LoginDto"];
                    "text/json": components["schemas"]["LoginDto"];
                    "application/*+json": components["schemas"]["LoginDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Help": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["HelpArticleSummaryDto"][];
                        "application/json": components["schemas"]["HelpArticleSummaryDto"][];
                        "text/json": components["schemas"]["HelpArticleSummaryDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Help/{slug}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    slug: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["HelpArticleDto"];
                        "application/json": components["schemas"]["HelpArticleDto"];
                        "text/json": components["schemas"]["HelpArticleDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Invitation/Create": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["InvitationCreateDto"];
                    "text/json": components["schemas"]["InvitationCreateDto"];
                    "application/*+json": components["schemas"]["InvitationCreateDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["WorkspaceInvitationDto"];
                        "application/json": components["schemas"]["WorkspaceInvitationDto"];
                        "text/json": components["schemas"]["WorkspaceInvitationDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Invitation/GetList": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    workspaceId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["WorkspaceInvitationDto"][];
                        "application/json": components["schemas"]["WorkspaceInvitationDto"][];
                        "text/json": components["schemas"]["WorkspaceInvitationDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Invitation/Revoke": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: {
                    id?: string;
                    workspaceId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Invitation/Preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    token?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["InvitationPreviewDto"];
                        "application/json": components["schemas"]["InvitationPreviewDto"];
                        "text/json": components["schemas"]["InvitationPreviewDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Invitation/Register": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["InvitationRegisterDto"];
                    "text/json": components["schemas"]["InvitationRegisterDto"];
                    "application/*+json": components["schemas"]["InvitationRegisterDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Invitation/Accept": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["InvitationAcceptDto"];
                    "text/json": components["schemas"]["InvitationAcceptDto"];
                    "application/*+json": components["schemas"]["InvitationAcceptDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Profile/Save": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["ProfileDto"];
                    "text/json": components["schemas"]["ProfileDto"];
                    "application/*+json": components["schemas"]["ProfileDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["ProfileDto"];
                        "application/json": components["schemas"]["ProfileDto"];
                        "text/json": components["schemas"]["ProfileDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Profile/SetAvatar": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["SetAvatarDto"];
                    "text/json": components["schemas"]["SetAvatarDto"];
                    "application/*+json": components["schemas"]["SetAvatarDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Profile/Details": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["ProfileDto"];
                        "application/json": components["schemas"]["ProfileDto"];
                        "text/json": components["schemas"]["ProfileDto"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Users/GetProfiles": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["ProfileSummaryDto"][];
                        "application/json": components["schemas"]["ProfileSummaryDto"][];
                        "text/json": components["schemas"]["ProfileSummaryDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Users/GetPermissions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    userId?: string;
                    workspaceId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["PermissionEnum"][];
                        "application/json": components["schemas"]["PermissionEnum"][];
                        "text/json": components["schemas"]["PermissionEnum"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Users/SetPermissions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["UserAndPermissionsDto"];
                    "text/json": components["schemas"]["UserAndPermissionsDto"];
                    "application/*+json": components["schemas"]["UserAndPermissionsDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Workspace/Create": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["WorkspaceDto"];
                    "text/json": components["schemas"]["WorkspaceDto"];
                    "application/*+json": components["schemas"]["WorkspaceDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["WorkspaceCreatedDto"];
                        "application/json": components["schemas"]["WorkspaceCreatedDto"];
                        "text/json": components["schemas"]["WorkspaceCreatedDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Workspace/Update": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": components["schemas"]["WorkspaceDto"];
                    "text/json": components["schemas"]["WorkspaceDto"];
                    "application/*+json": components["schemas"]["WorkspaceDto"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Workspace/SetPrimary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: {
                    workspaceId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Workspace/Leave": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: {
                    workspaceId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["AuthenticationResultDto"];
                        "application/json": components["schemas"]["AuthenticationResultDto"];
                        "text/json": components["schemas"]["AuthenticationResultDto"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Workspace/RemoveMember": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: {
                    userId?: string;
                    workspaceId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Workspace/GetList": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": components["schemas"]["WorkspaceDto"][];
                        "application/json": components["schemas"]["WorkspaceDto"][];
                        "text/json": components["schemas"]["WorkspaceDto"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/Workspace/MyPermissions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/plain": {
                            [key: string]: components["schemas"]["PermissionEnum"][];
                        };
                        "application/json": {
                            [key: string]: components["schemas"]["PermissionEnum"][];
                        };
                        "text/json": {
                            [key: string]: components["schemas"]["PermissionEnum"][];
                        };
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
}
export type webhooks = Record<string, never>;
export interface components {
    schemas: {
        AccountActivityDto: Record<string, never>;
        /**
         * Format: int32
         * @enum {integer}
         */
        AccountAdminActionKind: 1 | 2 | 3 | 4 | 5 | 6;
        AccountCardDto: {
            /** Format: uuid */
            userId: string;
            userName: string;
            /** Format: date-time */
            registeredAtUtc: string;
            hasPassword: boolean;
            hasTelegram: boolean;
            hasVk: boolean;
            canBeRemoved: boolean;
            activity: components["schemas"]["AccountActivityDto"];
            workspaces: components["schemas"]["AccountWorkspaceDto"][];
        };
        AccountLoginsDto: {
            userName: string;
            userNameIsGenerated: boolean;
            hasPassword: boolean;
            email: string | null;
            emailConfirmed: boolean;
            phone: string | null;
            comment: string | null;
            telegramLinked: boolean;
            vkLinked: boolean;
            /** Format: int32 */
            readonly loginCount: number;
            canSetPasswordWithoutCurrent: boolean;
            /** Format: date-time */
            freshLoginUntilUtc: string | null;
        };
        AccountSaveDto: {
            email?: string | null;
            phone?: string | null;
            comment?: string | null;
            language?: string | null;
        };
        AccountWorkspaceDto: {
            /** Format: uuid */
            id: string;
            name: string;
            isOwner: boolean;
            otherMembers: string[];
            readonly isSolo: boolean;
        };
        AdminAccessLogEntryDto: {
            /** Format: uuid */
            id: string;
            /** Format: date-time */
            createdUtc: string;
            /** Format: uuid */
            adminUserId: string | null;
            adminName: string | null;
            /** Format: uuid */
            runId: string;
            /** Format: uuid */
            workspaceId: string;
            workspaceName: string | null;
        };
        AdminAccessLogPageDto: {
            entries: components["schemas"]["AdminAccessLogEntryDto"][];
            /** Format: uuid */
            nextBefore: string | null;
        };
        AdminAccountActionDto: {
            kind: components["schemas"]["AccountAdminActionKind"];
            adminName: string | null;
            reason: string;
            /** Format: date-time */
            createdUtc: string;
        };
        AdminAccountCardDto: {
            card: components["schemas"]["AccountCardDto"];
            email: string | null;
            emailConfirmed: boolean;
            phoneNumber: string | null;
            /** Format: date-time */
            lastLoginAtUtc: string | null;
            isAdmin: boolean;
            actions: components["schemas"]["AdminAccountActionDto"][];
        };
        AdminAccountDto: {
            /** Format: uuid */
            userId: string;
            userName: string;
            email: string | null;
            phoneNumber: string | null;
            hasTelegram: boolean;
            hasVk: boolean;
            /** Format: date-time */
            registeredAtUtc: string;
        };
        AdminAccountJournalEntryDto: {
            /** Format: uuid */
            id: string;
            /** Format: date-time */
            createdUtc: string;
            kind: components["schemas"]["AccountAdminActionKind"] | null;
            /** Format: uuid */
            targetUserId: string;
            targetUserName: string;
            targetExists: boolean;
            /** Format: uuid */
            adminUserId: string | null;
            adminName: string | null;
            reason: string | null;
            /** Format: uuid */
            keptUserId: string | null;
            keptUserName: string | null;
            platform: string | null;
        };
        AdminAccountJournalPageDto: {
            entries: components["schemas"]["AdminAccountJournalEntryDto"][];
            /** Format: uuid */
            nextBefore: string | null;
        };
        AdminAccountReasonDto: {
            reason?: string | null;
        };
        AdminAssistantLimitDto: {
            /** Format: int64 */
            tokensPerDay: number | null;
            /** Format: int64 */
            tokensPerMonth: number | null;
            /** Format: double */
            costPerDay: number | null;
            /** Format: double */
            costPerMonth: number | null;
        };
        AdminBillingModelDto: {
            /** Format: uuid */
            modelId: string;
            displayName: string;
            provider: string;
            /** Format: int32 */
            runs: number;
            /** Format: int64 */
            tokens: number;
            /** Format: double */
            cost: number;
            /** Format: int64 */
            serviceTokens: number;
            /** Format: double */
            serviceCost: number;
        };
        AdminBillingProviderDto: {
            provider: string;
            facts: components["schemas"]["AdminLedgerFactDto"][];
            /** Format: double */
            fact: number | null;
            /** Format: double */
            estimate: number;
            /** Format: double */
            discrepancy: number | null;
            account: components["schemas"]["AdminProviderAccountDto"];
        };
        AdminBillingSummaryDto: {
            /** Format: date */
            from: string;
            /** Format: date */
            to: string;
            /** Format: uuid */
            modelId: string | null;
            currency: string;
            /** Format: int32 */
            runs: number;
            /** Format: int64 */
            tokens: number;
            /** Format: double */
            cost: number;
            /** Format: double */
            serviceCost: number;
            service: components["schemas"]["AdminServiceSpendDto"][];
            /** Format: double */
            totalCost: number;
            defaultLimit: components["schemas"]["AdminAssistantLimitDto"];
            models: components["schemas"]["AdminBillingModelDto"][];
            workspaces: components["schemas"]["AdminBillingWorkspaceDto"][];
            providers: components["schemas"]["AdminBillingProviderDto"][];
        };
        AdminBillingUserDto: {
            /** Format: uuid */
            userId: string;
            name: string | null;
            /** Format: int32 */
            runs: number;
            /** Format: int64 */
            tokens: number;
            /** Format: double */
            cost: number;
        };
        AdminBillingWorkspaceCardDto: {
            /** Format: uuid */
            workspaceId: string;
            workspaceName: string;
            currency: string;
            ownLimit: components["schemas"]["AdminAssistantLimitDto"] | null;
            defaultLimit: components["schemas"]["AdminAssistantLimitDto"];
            limit: components["schemas"]["AdminAssistantLimitDto"];
            today: components["schemas"]["AdminSpendUsageDto"];
            month: components["schemas"]["AdminSpendUsageDto"];
            /** Format: date */
            from: string;
            /** Format: date */
            to: string;
            users: components["schemas"]["AdminBillingUserDto"][];
            /** Format: date-time */
            limitUpdatedUtc: string | null;
            limitUpdatedBy: string | null;
        };
        AdminBillingWorkspaceDto: {
            /** Format: uuid */
            workspaceId: string;
            workspaceName: string | null;
            /** Format: int32 */
            runs: number;
            /** Format: int64 */
            tokens: number;
            /** Format: double */
            cost: number;
            ownLimit: boolean;
        };
        AdminDialogDto: {
            /** Format: uuid */
            runId: string;
            /** Format: date-time */
            startedUtc: string;
            /** Format: date-time */
            finishedUtc: string | null;
            /** Format: uuid */
            workspaceId: string;
            workspaceName: string;
            /** Format: uuid */
            modelId: string | null;
            modelName: string | null;
            status: components["schemas"]["AssistantRunStatus"];
            stopReason: components["schemas"]["AssistantStopReason"] | null;
            /** Format: int32 */
            steps: number;
            /** Format: int64 */
            tokensIn: number;
            /** Format: int64 */
            tokensOut: number;
            /** Format: double */
            cost: number;
            /** Format: int32 */
            clarifications: number | null;
            /** Format: int32 */
            satisfaction: number | null;
            traceExpired: boolean;
        };
        AdminDialogEvaluationDto: {
            modelName: string;
            succeeded: boolean | null;
            /** Format: int32 */
            satisfaction: number | null;
            /** Format: int32 */
            clarifications: number | null;
            /** Format: int32 */
            replans: number | null;
            agentMistake: boolean | null;
            stopReason: components["schemas"]["DialogStopReason"] | null;
            notes: string | null;
            /** Format: date-time */
            createdUtc: string;
        };
        AdminDialogPageDto: {
            dialogs: components["schemas"]["AdminDialogDto"][];
            /** Format: uuid */
            nextBefore: string | null;
        };
        AdminDialogStepDto: {
            /** Format: int32 */
            step: number;
            kind: string;
            toolName: string | null;
            summary: string;
            input: string;
            output: string | null;
            /** Format: int32 */
            durationMs: number;
            /** Format: int32 */
            tokensIn: number;
            /** Format: int32 */
            tokensOut: number;
            error: string | null;
            /** Format: date-time */
            createdUtc: string;
        };
        AdminDialogTraceDto: {
            dialog: components["schemas"]["AdminDialogDto"];
            expired: boolean;
            steps: components["schemas"]["AdminDialogStepDto"][];
            evaluation: components["schemas"]["AdminDialogEvaluationDto"] | null;
        };
        AdminDto: {
            /** Format: uuid */
            userId: string;
            userName: string;
            roles: components["schemas"]["AdminRole"][];
        };
        AdminEvaluationStartDto: {
            /** Format: uuid */
            modelId: string;
            /** Format: int32 */
            batchSize: number;
        };
        AdminEvaluationsDto: {
            /** Format: int32 */
            maxBatchSize: number;
            jobs: components["schemas"]["AdminJobDto"][];
        };
        AdminInvitationCreateDto: {
            roles: components["schemas"]["AdminRole"][];
        };
        AdminInvitationDto: {
            /** Format: uuid */
            id: string;
            roles: components["schemas"]["AdminRole"][];
            status: components["schemas"]["AdminInvitationStatus"];
            /** Format: date-time */
            createdUtc: string;
            /** Format: date-time */
            expiresUtc: string;
            createdByName: string | null;
            acceptedByName: string | null;
            revokedByName: string | null;
        };
        AdminInvitationIssuedDto: {
            /** Format: uuid */
            id: string;
            roles: components["schemas"]["AdminRole"][];
            status: components["schemas"]["AdminInvitationStatus"];
            /** Format: date-time */
            createdUtc: string;
            /** Format: date-time */
            expiresUtc: string;
            createdByName: string | null;
            acceptedByName: string | null;
            revokedByName: string | null;
            /** Format: uuid */
            token: string;
        };
        AdminInvitationPreviewDto: {
            status: components["schemas"]["AdminInvitationStatus"];
            inviterName: string | null;
            roles: components["schemas"]["AdminRole"][];
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        AdminInvitationStatus: 0 | 1 | 2 | 3;
        AdminIssuedResetDto: {
            url: string;
            /** Format: date-time */
            expiresAtUtc: string;
            replaced: boolean;
        };
        AdminJobDto: {
            /** Format: uuid */
            id: string;
            kind: components["schemas"]["AdminJobKind"];
            /** Format: uuid */
            modelId: string;
            modelName: string;
            startedBy: string | null;
            /** Format: date-time */
            createdUtc: string;
            /** Format: date-time */
            finishedUtc: string | null;
            status: components["schemas"]["AdminJobStatus"];
            /** Format: int32 */
            planned: number;
            /** Format: int32 */
            done: number;
            /** Format: int32 */
            skipped: number;
            /** Format: int32 */
            failed: number;
            /** Format: int64 */
            tokensIn: number;
            /** Format: int64 */
            tokensCached: number;
            /** Format: int64 */
            tokensOut: number;
            /** Format: double */
            cost: number;
            error: string | null;
            report: components["schemas"]["ModelCheckReportDto"] | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        AdminJobKind: 0 | 2 | 3;
        /**
         * Format: int32
         * @enum {integer}
         */
        AdminJobStatus: 0 | 1 | 2;
        AdminLedgerEntryDto: {
            provider: string;
            kind: components["schemas"]["ProviderLedgerKind"];
            /** Format: double */
            amount: number;
            /** Format: date */
            periodFrom?: string | null;
            /** Format: date */
            periodTo?: string | null;
            comment?: string | null;
        };
        AdminLedgerFactDto: {
            /** Format: uuid */
            id: string;
            /** Format: date */
            periodFrom: string;
            /** Format: date */
            periodTo: string;
            /** Format: double */
            amount: number;
            comment: string | null;
            /** Format: date-time */
            createdUtc: string;
            actorName: string | null;
        };
        AdminLoginResultDto: {
            token: string;
            /** Format: date-time */
            expiresUtc: string;
            admin: components["schemas"]["AdminDto"];
        };
        AdminMemberDto: {
            /** Format: uuid */
            userId: string;
            userName: string;
            roles: components["schemas"]["AdminMemberRoleDto"][];
        };
        AdminMemberRoleDto: {
            role: components["schemas"]["AdminRole"];
            /** Format: date-time */
            grantedUtc: string;
            grantedByName: string | null;
        };
        AdminModelDto: {
            /** Format: uuid */
            id: string;
            provider: string;
            key: string;
            displayName: string;
            /** Format: int32 */
            priority: number;
            /** Format: double */
            costIn: number;
            /** Format: double */
            costCached: number;
            /** Format: double */
            costOut: number;
            isEnabled: boolean;
            isLocal: boolean;
            /** Format: date-time */
            openUntilUtc: string | null;
            /** Format: int32 */
            failureCount: number;
            isUsed: boolean;
            /** Format: date-time */
            updatedUtc: string;
            updatedBy: string | null;
            /** Format: int32 */
            version: number;
        };
        AdminModelEditDto: {
            provider: string;
            key: string;
            displayName: string;
            /** Format: double */
            costIn: number;
            /** Format: double */
            costCached: number;
            /** Format: double */
            costOut: number;
            isEnabled: boolean;
            isLocal: boolean;
            /** Format: int32 */
            version: number;
        };
        AdminModelsDto: {
            currency: string;
            models: components["schemas"]["AdminModelDto"][];
            transcription: components["schemas"]["AdminTranscriptionModelDto"] | null;
        };
        AdminProviderAccountDto: {
            hasPayments: boolean;
            /** Format: double */
            paid: number;
            /** Format: double */
            fact: number;
            /** Format: double */
            unbilled: number;
            /** Format: double */
            remaining: number;
        };
        AdminReleaseEmailDto: {
            email?: string | null;
            reason?: string | null;
        };
        AdminReleasePhoneDto: {
            phone?: string | null;
            reason?: string | null;
        };
        AdminReleasedContactDto: {
            releasedFromUserName: string;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        AdminRole: 0 | 1 | 2 | 4 | 8 | 16;
        AdminServiceSpendDto: {
            kind: components["schemas"]["ServiceSpendKind"];
            /** Format: int32 */
            calls: number;
            /** Format: int64 */
            tokens: number;
            /** Format: double */
            cost: number;
        };
        AdminSpendUsageDto: {
            /** Format: int64 */
            tokens: number;
            /** Format: double */
            cost: number;
        };
        AdminSummaryDto: {
            version: string;
            /** Format: date-time */
            serverTimeUtc: string;
        };
        AdminTranscriptionModelDto: {
            provider: string;
            model: string;
            /** Format: double */
            costPerMinute: number;
        };
        AssistantFactDto: {
            /** Format: uuid */
            id: string;
            kind: components["schemas"]["AssistantFactKind"];
            text: string;
            personal: boolean;
            /** Format: date-time */
            createdAtUtc: string;
            /** Format: uuid */
            sourceRunId: string | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        AssistantFactKind: 0 | 1 | 2;
        AssistantHistoryDto: {
            messages: components["schemas"]["AssistantMessageDto"][];
            activeRun: components["schemas"]["AssistantRunDto"] | null;
            /** Format: uuid */
            nextCursor: string | null;
        };
        AssistantMemoryDto: {
            shared: components["schemas"]["AssistantFactDto"][];
            personal: components["schemas"]["AssistantFactDto"][];
        };
        AssistantMessageDto: {
            /** Format: uuid */
            id: string;
            role: components["schemas"]["AssistantMessageRole"];
            content: string;
            /** Format: uuid */
            runId: string | null;
            plan: components["schemas"]["AssistantPlanDto"] | null;
            options: string[] | null;
            facts: components["schemas"]["AssistantFactDto"][];
            /** Format: date-time */
            createdAtUtc: string;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        AssistantMessageRole: 0 | 1 | 2;
        AssistantPlanDto: {
            /** Format: uuid */
            id: string;
            status: components["schemas"]["AssistantPlanStatus"];
            operations: components["schemas"]["PlanDocument"];
            result: components["schemas"]["PlanResult"] | null;
            /** Format: date-time */
            appliedAtUtc: string | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        AssistantPlanStatus: 0 | 1 | 2 | 3;
        AssistantRunDto: {
            /** Format: uuid */
            id: string;
            status: components["schemas"]["AssistantRunStatus"];
            /** Format: int32 */
            step: number;
            /** Format: int32 */
            stepsHard: number;
            escalated: boolean;
            currentActivity: string | null;
            stopReason: components["schemas"]["AssistantStopReason"] | null;
            /** Format: date-time */
            finishedAtUtc: string | null;
            /** Format: uuid */
            messageId: string | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        AssistantRunStatus: 0 | 1 | 2 | 3;
        AssistantSendDto: {
            text: string;
        };
        AssistantSendResultDto: {
            /** Format: uuid */
            runId: string;
            /** Format: uuid */
            messageId: string;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        AssistantStopReason: 0 | 1 | 2 | 3 | 4;
        AssistantTraceDto: {
            expired: boolean;
            steps: components["schemas"]["AssistantTraceStepDto"][];
        };
        AssistantTraceStepDto: {
            /** Format: int32 */
            step: number;
            kind: string;
            toolName: string | null;
            summary: string;
            /** Format: int32 */
            durationMs: number;
            error: string | null;
        };
        AssistantTranscriptionDto: {
            text: string;
        };
        AuthenticationResultDto: {
            token: string;
            profile: components["schemas"]["ProfileDto"];
            permissionsByWorkspace: {
                [key: string]: components["schemas"]["PermissionEnum"][];
            };
            isNewAccount: boolean;
            mergePreview: components["schemas"]["MergePreviewDto"] | null;
        };
        ChangePasswordDto: {
            /** Format: password */
            currentPassword: string;
            /** Format: password */
            newPassword: string;
            /** Format: password */
            newPasswordConfirm: string;
        };
        ChangeUserNameDto: {
            userName: string;
        };
        ConfirmPasswordDto: {
            /** Format: password */
            password: string;
        };
        DeleteAccountDto: {
            userName: string;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        DialogStopReason: 0 | 1 | 2 | 3;
        EmailTokenDto: {
            token: string;
        };
        ExternalCodeDto: {
            code?: string | null;
            verifier?: string | null;
            deviceId?: string | null;
            state?: string | null;
            language?: string | null;
            existingOnly: boolean;
        };
        ExternalLoginsConfigDto: {
            telegramBotId: string;
            telegramAuthorizationEndpoint: string;
            telegramRedirectUri: string;
            vkAppId: string;
            vkAuthorizationEndpoint: string;
            vkRedirectUri: string;
        };
        ExternalProofDto: {
            provider: components["schemas"]["ExternalProvider"];
            code?: string | null;
            verifier?: string | null;
            deviceId?: string | null;
            state?: string | null;
            initData?: string | null;
            vkLaunchParams?: string | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        ExternalProvider: 1 | 2;
        HelpArticleDto: {
            slug: string;
            title: string;
            markdown: string;
        };
        HelpArticleSummaryDto: {
            slug: string;
            title: string;
        };
        InvitationAcceptDto: {
            /** Format: uuid */
            token: string;
        };
        InvitationCreateDto: {
            /** Format: uuid */
            workspaceId: string;
            inviteeName?: string | null;
            permissions: components["schemas"]["PermissionEnum"][];
        };
        InvitationPreviewDto: {
            status: components["schemas"]["InvitationStatusEnum"];
            workspaceName: string | null;
            inviterName: string | null;
            inviteeName: string | null;
        };
        InvitationRegisterDto: {
            /** Format: uuid */
            token: string;
            userName: string;
            /** Format: password */
            password: string;
            /** Format: password */
            passwordConfirm: string;
            language?: string | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        InvitationStatusEnum: 0 | 1 | 2 | 3;
        LinkResultDto: {
            logins: components["schemas"]["AccountLoginsDto"] | null;
            mergePreview: components["schemas"]["MergePreviewDto"] | null;
        };
        LogInAndLinkDto: {
            login: string;
            password: string;
            proof: components["schemas"]["ExternalProofDto"];
        };
        LoginDto: {
            login: string;
            /** Format: password */
            password: string;
            readonly isPhone: boolean;
            readonly isEmail: boolean;
        };
        MergeDto: {
            /** Format: uuid */
            ticket: string;
            /** Format: uuid */
            keepUserId: string;
        };
        MergePreviewDto: {
            /** Format: uuid */
            ticket: string;
            /** Format: date-time */
            expiresAtUtc: string;
            current: components["schemas"]["AccountCardDto"];
            other: components["schemas"]["AccountCardDto"];
            canKeepOther: boolean;
        };
        MergeProofDto: {
            login?: string | null;
            password?: string | null;
            external?: components["schemas"]["ExternalProofDto"] | null;
        };
        ModelCheckPhraseDto: {
            phrase: string;
            expectedTool: string | null;
            tools: string[];
            answer: string;
            error: string | null;
            /** Format: int32 */
            steps: number;
            /** Format: int64 */
            tokensIn: number;
            /** Format: int64 */
            tokensOut: number;
            /** Format: double */
            cost: number;
            toolHit: boolean;
            cleanAnswer: boolean | null;
            russianAnswer: boolean;
        };
        ModelCheckReportDto: {
            /** Format: int32 */
            phrases: number;
            shares: components["schemas"]["ModelCheckShareDto"][];
            /** Format: double */
            averageSteps: number;
            results: components["schemas"]["ModelCheckPhraseDto"][];
        };
        ModelCheckShareDto: {
            key: string;
            /** Format: int32 */
            hits: number;
            /** Format: int32 */
            considered: number;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        PermissionEnum: 0 | 1 | 2 | 3 | 4;
        PlanDocument: {
            operations: components["schemas"]["PlanOperation"][];
        };
        PlanOperation: {
            /** Format: int32 */
            index: number;
            kind: string;
            args: unknown;
        };
        PlanOperationResult: {
            /** Format: int32 */
            index: number;
            done: boolean;
            createdId: string | null;
            error: string | null;
        };
        PlanResult: {
            operations: components["schemas"]["PlanOperationResult"][];
        };
        ProfileDto: {
            /** Format: uuid */
            id: string;
            userName: string | null;
            userNameIsGenerated: boolean;
            hasPassword: boolean;
            phone: string | null;
            email: string | null;
            emailConfirmed: boolean;
            language: string;
            /** Format: int32 */
            avatarId: number | null;
            comment: string | null;
            isAdult: boolean;
            hasAcceptedTerms: boolean;
            /** Format: uuid */
            primaryWorkspaceId: string | null;
        };
        ProfileSummaryDto: {
            /** Format: uuid */
            id: string;
            userName: string | null;
            /** Format: int32 */
            avatarId: number | null;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        ProviderLedgerKind: 0 | 1;
        RegisterDto: {
            userName: string;
            phone?: string | null;
            email?: string | null;
            /** Format: password */
            password: string;
            /** Format: password */
            passwordConfirm: string;
            workspaceName?: string | null;
            language?: string | null;
        };
        RegisterExternalDto: {
            proof: components["schemas"]["ExternalProofDto"];
            language?: string | null;
        };
        RequestResetDto: {
            /** Format: email */
            email: string;
        };
        ResetPasswordDto: {
            token: string;
            /** Format: password */
            newPassword: string;
            /** Format: password */
            newPasswordConfirm: string;
        };
        /**
         * Format: int32
         * @enum {integer}
         */
        ServiceSpendKind: 0 | 1 | 2 | 3;
        SetAvatarDto: {
            /** Format: int32 */
            avatarId: number;
        };
        SetLanguageDto: {
            language: string;
        };
        SetPasswordDto: {
            /** Format: password */
            newPassword: string;
            /** Format: password */
            newPasswordConfirm: string;
        };
        TelegramAuthDto: {
            initData?: string | null;
            language?: string | null;
        };
        UnlinkDto: {
            provider: components["schemas"]["ExternalProvider"];
        };
        UserAndPermissionsDto: {
            /** Format: uuid */
            userId: string;
            /** Format: uuid */
            workspaceId: string;
            permissions: components["schemas"]["PermissionEnum"][];
        };
        VkAuthDto: {
            launchParams?: string | null;
            /** Format: uuid */
            invitation?: string | null;
            language?: string | null;
        };
        WorkspaceCreatedDto: {
            /** Format: uuid */
            workspaceId: string;
            authentication: components["schemas"]["AuthenticationResultDto"];
        };
        WorkspaceDto: {
            /** Format: uuid */
            id: string;
            name: string;
            /** Format: int32 */
            membersCount: number | null;
        };
        WorkspaceInvitationDto: {
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            token: string;
            inviteeName: string | null;
            status: components["schemas"]["InvitationStatusEnum"];
            /** Format: date-time */
            createdAtUtc: string;
            /** Format: date-time */
            expiresAtUtc: string;
            acceptedByUserName: string | null;
            permissions: components["schemas"]["PermissionEnum"][];
        };
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export type operations = Record<string, never>;

/** Схема DTO по имени: `ApiSchema<'ProfileDto'>` */
export type ApiSchema<Name extends keyof components['schemas']> = components['schemas'][Name];
