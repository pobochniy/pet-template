import {Injectable, signal} from "@angular/core";
import {AlertModel} from "../models/alert.model";

@Injectable({providedIn: 'root'})
export class AlertsService {
  private nextId = 0;
  public alerts = signal<AlertModel[]>([]);

  public push(alertClass: "primary" | "secondary" | "success" | "danger" | "warning" | "info" | "light" | "dark"
    , content: string
    , timeToCloseMs: number = 0) {

    const id = ++this.nextId;
    this.alerts.update(current => [...current, new AlertModel({id: id, alertClass: alertClass, content: content})]);
    if (timeToCloseMs > 0) setTimeout(() => {
      this.remove(id);
    }, timeToCloseMs)
  }

  public remove(id: number) {
    this.alerts.update(current => current.filter(x => x.id !== id));
  }
}
