import { ProfileForm, createProfileForm } from './profile-form.model';

describe('createProfileForm', () => {
  let profileFormModel: ProfileForm;

  beforeEach(() => {
    profileFormModel = createProfileForm();
  });

  it('should create form with all required controls', () => {
    // Логин и комментарий — в блоке «Основное» (`account-form.model.ts`)
    expect(profileFormModel.contains('userName')).toBe(false);
    expect(profileFormModel.contains('isAdult')).toBe(true);
    expect(profileFormModel.contains('hasAcceptedTerms')).toBe(true);
  });

  describe('isAdult validation', () => {
    it('should be invalid when false', () => {
      const control = profileFormModel.get('isAdult');
      control?.setValue(false);
      expect(control?.valid).toBe(false);
      expect(control?.hasError('required')).toBe(true);
    });

    it('should be valid when true', () => {
      const control = profileFormModel.get('isAdult');
      control?.setValue(true);
      expect(control?.valid).toBe(true);
    });
  });

  describe('hasAcceptedTerms validation', () => {
    it('should be invalid when false', () => {
      const control = profileFormModel.get('hasAcceptedTerms');
      control?.setValue(false);
      expect(control?.valid).toBe(false);
      expect(control?.hasError('required')).toBe(true);
    });

    it('should be valid when true', () => {
      const control = profileFormModel.get('hasAcceptedTerms');
      control?.setValue(true);
      expect(control?.valid).toBe(true);
    });
  });

  describe('form validity', () => {
    it('should be invalid when all fields are empty/false', () => {
      expect(profileFormModel.valid).toBe(false);
    });

    it('should be valid when both confirmations are given', () => {
      profileFormModel.setValue({ isAdult: true, hasAcceptedTerms: true });
      expect(profileFormModel.valid).toBe(true);
    });

    it('should be invalid if isAdult is false', () => {
      profileFormModel.setValue({ isAdult: false, hasAcceptedTerms: true });
      expect(profileFormModel.valid).toBe(false);
    });

    it('should be invalid if hasAcceptedTerms is false', () => {
      profileFormModel.setValue({ isAdult: true, hasAcceptedTerms: false });
      expect(profileFormModel.valid).toBe(false);
    });
  });
});
