import { profileFormModel } from './profile-form.model';

describe('profileFormModel', () => {
  beforeEach(() => {
    // Сбрасываем форму перед каждым тестом
    profileFormModel.reset();
  });

  it('should create form with all required controls', () => {
    expect(profileFormModel.contains('userName')).toBe(true);
    expect(profileFormModel.contains('comment')).toBe(true);
    expect(profileFormModel.contains('isAdult')).toBe(true);
    expect(profileFormModel.contains('hasAcceptedTerms')).toBe(true);
  });

  describe('userName validation', () => {
    it('should be invalid when empty', () => {
      const control = profileFormModel.get('userName');
      control?.setValue('');
      expect(control?.valid).toBe(false);
      expect(control?.hasError('required')).toBe(true);
    });

    it('should be invalid when less than 2 characters', () => {
      const control = profileFormModel.get('userName');
      control?.setValue('a');
      expect(control?.valid).toBe(false);
      expect(control?.hasError('minlength')).toBe(true);
    });

    it('should be invalid when more than 20 characters', () => {
      const control = profileFormModel.get('userName');
      control?.setValue('a'.repeat(21));
      expect(control?.valid).toBe(false);
      expect(control?.hasError('maxlength')).toBe(true);
    });

    it('should be valid with correct length', () => {
      const control = profileFormModel.get('userName');
      control?.setValue('ValidUser');
      expect(control?.valid).toBe(true);
    });

    it('should be valid with exactly 2 characters', () => {
      const control = profileFormModel.get('userName');
      control?.setValue('ab');
      expect(control?.valid).toBe(true);
    });

    it('should be valid with exactly 20 characters', () => {
      const control = profileFormModel.get('userName');
      control?.setValue('a'.repeat(20));
      expect(control?.valid).toBe(true);
    });
  });

  describe('comment validation', () => {
    it('should be valid when empty', () => {
      const control = profileFormModel.get('comment');
      control?.setValue('');
      expect(control?.valid).toBe(true);
    });

    it('should be valid with text', () => {
      const control = profileFormModel.get('comment');
      control?.setValue('This is a comment');
      expect(control?.valid).toBe(true);
    });

    it('should be invalid when more than 3000 characters', () => {
      const control = profileFormModel.get('comment');
      control?.setValue('a'.repeat(3001));
      expect(control?.valid).toBe(false);
      expect(control?.hasError('maxlength')).toBe(true);
    });

    it('should be valid with exactly 3000 characters', () => {
      const control = profileFormModel.get('comment');
      control?.setValue('a'.repeat(3000));
      expect(control?.valid).toBe(true);
    });
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

    it('should be valid when all required fields are filled correctly', () => {
      profileFormModel.setValue({
        userName: 'TestUser',
        comment: 'Test comment',
        isAdult: true,
        hasAcceptedTerms: true,
      });
      expect(profileFormModel.valid).toBe(true);
    });

    it('should be valid with empty comment', () => {
      profileFormModel.setValue({
        userName: 'TestUser',
        comment: '',
        isAdult: true,
        hasAcceptedTerms: true,
      });
      expect(profileFormModel.valid).toBe(true);
    });

    it('should be invalid if userName is missing', () => {
      profileFormModel.patchValue({
        userName: '',
        comment: 'Test',
        isAdult: true,
        hasAcceptedTerms: true,
      });
      expect(profileFormModel.valid).toBe(false);
    });

    it('should be invalid if isAdult is false', () => {
      profileFormModel.patchValue({
        userName: 'TestUser',
        comment: 'Test',
        isAdult: false,
        hasAcceptedTerms: true,
      });
      expect(profileFormModel.valid).toBe(false);
    });

    it('should be invalid if hasAcceptedTerms is false', () => {
      profileFormModel.patchValue({
        userName: 'TestUser',
        comment: 'Test',
        isAdult: true,
        hasAcceptedTerms: false,
      });
      expect(profileFormModel.valid).toBe(false);
    });
  });
});
