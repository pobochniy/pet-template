/**
 * Сгенерировано `npm run i18n` из `i18n/`. Руками не правим — правка уедет
 * при следующем запуске кодогена, а CI отобьёт расхождение с исходником.
 */

export default {
  "invite.error.accepted": "Someone has already signed up with this link. If it was you, log in with your username. If not, ask for a new link",
  "invite.error.alreadyMember": "You are already a member of this space",
  "invite.error.cannotRevokeAccepted": "An accepted invitation cannot be revoked",
  "invite.error.emptyPermissions": "An invitation without permissions does not let anyone into the space",
  "invite.error.expired": "The link is invalid or has expired. Ask for a new one",
  "invite.error.notFound": "Invitation not found",
  "invite.error.revoked": "The invitation has been revoked. Ask for a new link",
  "invite.error.workspaceNotFound": "Space not found",
  "invite.field.login": "Username, email or phone",
  "invite.page.accept": "Accept the invitation",
  "invite.page.acceptFailed": "Could not accept the invitation",
  "invite.page.greeting": "{inviter} invites you to the space “{workspace}”",
  "invite.page.greetingName": "Hi, {name}!",
  "invite.page.greetingNoInviter": "You are invited to the space “{workspace}”",
  "invite.page.haveAccount": "I already have an account",
  "invite.page.loading": "Checking the invitation…",
  "invite.page.loginSubmit": "Log in and accept",
  "invite.page.notRegistered": "I have not registered yet",
  "invite.page.registerSubmit": "Sign up and log in",
  "invite.page.toHome": "Home",
  "invite.page.unavailable": "Could not check the invitation: the server did not respond. The link may still be valid — try again",
} as const;
