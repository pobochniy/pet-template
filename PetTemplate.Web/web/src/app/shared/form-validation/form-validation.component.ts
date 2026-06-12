import { Component, Input, TemplateRef, ViewContainerRef, Directive, AfterViewInit } from '@angular/core';
import { AbstractControl } from '@angular/forms';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'form-validation',
  templateUrl: './form-validation.component.html',
  standalone: true,
  imports: [CommonModule]
})
export class FormValidationComponent implements AfterViewInit {
  @Input() model!: AbstractControl;
  @Input() fieldName!: string;

  constructor() {

  }

  ngAfterViewInit(): void {
    console.log(this.model);
  }
}
