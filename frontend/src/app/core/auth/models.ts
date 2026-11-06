/** What login, refresh and change-password answer. The refresh token itself stays in its cookie. */
export interface AccessTokenDto {
  accessToken: string;
  expiresAt: string;
  mustChangePassword: boolean;
}

/** GET /api/auth/me: the user, and the permissions their role has now. */
export interface CurrentUserDto {
  id: string;
  email: string;
  displayName: string;
  roleName: string;
  mustChangePassword: boolean;
  permissions: string[];
}
