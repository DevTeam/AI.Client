{ The application icon is square; center it in the wizard's portrait image area. }
procedure CenterApplicationImage(Image: TBitmapImage);
begin
  Image.Top := Image.Top + (Image.Height - Image.Width) div 2;
  Image.Height := Image.Width;
  Image.Anchors := [akLeft, akTop];
end;

procedure InitializeWizard;
begin
  CenterApplicationImage(WizardForm.WizardBitmapImage);
  CenterApplicationImage(WizardForm.WizardBitmapImage2);
end;
