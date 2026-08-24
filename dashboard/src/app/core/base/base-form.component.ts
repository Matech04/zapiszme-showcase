import { Component, inject, OnInit } from "@angular/core";
import { FormBuilder, FormGroup } from "@angular/forms";
import { DynamicDialogConfig, DynamicDialogRef } from "primeng/dynamicdialog";

@Component({ template: '' })
export abstract class BaseFormComponent<T> implements OnInit {
  protected fb = inject(FormBuilder);
  protected ref = inject(DynamicDialogRef);
  protected config = inject(DynamicDialogConfig);

  abstract form: FormGroup;

  ngOnInit(): void {

    const dataToEdit = this.config.data?.formData;

    if (dataToEdit) {
      this.form.patchValue(dataToEdit);
    }
  }

  save(): void {
    if (this.form.valid) {
      this.ref.close(this.form.value);
    }
    else {
      this.form.markAllAsTouched();
    }
  }

  cancel(): void {
    this.ref.close();
  }
}