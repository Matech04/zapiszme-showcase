import { Component, inject, OnInit, signal } from "@angular/core";
import { MessageService } from "primeng/api";

@Component({ template: '' })
export abstract class BaseReadComponent<T extends { id: string }> implements OnInit {

  items = signal<T[]>([]);
  isLoading = signal(false);
  protected messageService = inject(MessageService);

  protected abstract loadData(): void;

  ngOnInit(): void {
    this.loadData();
  }

  protected showToast(msg: string, severity: 'success' | 'info' | 'error' = 'info') {
    this.messageService.add({ severity, summary: 'Info', detail: msg });
  }

}