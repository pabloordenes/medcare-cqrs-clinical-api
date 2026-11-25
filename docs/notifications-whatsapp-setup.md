# Guía de Configuración WhatsApp Cloud API (Meta) — MedCareOS

## 1. Regla de Ventana de 24 Horas y Privacidad
Meta exige **plantillas pre-aprobadas (`Message Templates`)** siempre que MedCareOS inicie una conversación con un paciente o responda fuera de la ventana de 24 horas desde el último mensaje entrante del paciente.

- **Categoría obligatoria:** `UTILITY` (nunca `MARKETING`). Evitar palabras promocionales ("gratis", "oferta exclusiva", "promoción") para prevenir reclasificación automática a `MARKETING`.
- **Idioma:** `es` (o `es_CL` según disponibilidad de la cuenta WABA; configurable en `Notifications:WhatsApp:DefaultTemplateLanguage`).
- **Privacidad clínica:** Ninguna plantilla incluye síntomas (`SymptomsRaw`, `ProcessedSymptoms`), resúmenes NLP, diagnósticos ni RUT. Solo nombre de pila, centro, fecha, hora y especialidad (desactivable vía `Notifications:IncludeSpecialtyInMessages=false` o `SensitiveSpecialtiesExcluded`).

---

## 2. Catálogo de 7 Plantillas `UTILITY` Requeridas (`v1`)

| # | `TemplateKey` interno | Nombre en Meta Manager | Parámetros ordenados (`{{1}}..{{n}}`) | Botones | Copy exacto (`es-CL`) |
|---|---|---|---|---|---|
| 1 | `appointment.created` | `medcare_cita_agendada_v1` | `1:nombre`, `2:centro`, `3:fecha`, `4:hora`, `5:especialidad` | Ninguno | Hola `{{1}}`, tu cita en `{{2}}` fue agendada para el `{{3}}` a las `{{4}}` (`{{5}}`). Te enviaremos un recordatorio antes de tu hora. |
| 2 | `appointment.reminder` | `medcare_recordatorio_cita_v1` | `1:nombre`, `2:centro`, `3:fecha`, `4:hora`, `5:especialidad` | Quick Reply: `Confirmar` / `Cancelar` | Hola `{{1}}`, te recordamos tu cita en `{{2}}` el `{{3}}` a las `{{4}}` (`{{5}}`). Por favor confirma o cancela tu asistencia. |
| 3 | `appointment.final_confirmation` | `medcare_confirmacion_final_v1` | `1:nombre`, `2:fecha`, `3:hora`, `4:centro`, `5:deadline` | Quick Reply: `Confirmar` / `Cancelar` | Hola `{{1}}`, confirma tu hora del `{{2}}` a las `{{3}}` en `{{4}}` antes de `{{5}}`. Si no confirmas, podríamos asignar tu horario a otro paciente (sobrecupo) y tendrás que reagendar por tu cuenta. Si ambos pacientes llegan, se atenderá primero a quien haya confirmado antes. |
| 4 | `appointment.overbook_notice` | `medcare_aviso_sobrecupo_v1` | `1:nombre`, `2:fecha`, `3:hora`, `4:centro` | Quick Reply: `Confirmar` / `Cancelar` | Hola `{{1}}`, tu hora del `{{2}}` a las `{{3}}` en `{{4}}` pasó a modalidad sobrecupo por falta de confirmación previa. Si aún asistirás, confirma ahora; si ambos pacientes llegan, se atenderá primero a quien haya confirmado antes. |
| 5 | `waiting_list.offer` | `medcare_cupo_lista_espera_v1` | `1:nombre`, `2:centro`, `3:especialidad`, `4:fecha`, `5:hora`, `6:deadline` | Quick Reply: `Tomar cupo` / `Rechazar` | Hola `{{1}}`, se habilitó un cupo en `{{2}}` (`{{3}}`) para el `{{4}}` a las `{{5}}`. Confirma antes de `{{6}}` si deseas tomar este horario. |
| 6 | `waiting_list.overbook_offer_cancelled` | `medcare_cupo_cancelado_v1` | `1:nombre`, `2:fecha`, `3:hora`, `4:centro` | Ninguno | Hola `{{1}}`, lamentamos informarte que el cupo disponible para el `{{2}}` a las `{{3}}` en `{{4}}` se desactivó porque el paciente original confirmó su asistencia. Mantienes tu prioridad en nuestra lista de espera. |
| 7 | `appointment.reschedule_help` | `medcare_ayuda_reagendamiento_v1` | `1:nombre`, `2:fecha`, `3:hora`, `4:centro`, URL suffix `{{1}}` | URL dinámica: `https://app.medcare.cl/appointments/reschedule{{1}}` (`Reagendar hora`) | Hola `{{1}}`, tu horario del `{{2}}` a las `{{3}}` en `{{4}}` fue asignado a otro paciente por sobrecupo. Puedes elegir una nueva hora directamente desde el enlace adjunto. |

---

## 3. Trámite de Aprobación ante Meta (Iniciar Inmediatamente)

1. **Día 1 — Crear App Meta Business:** En [Meta for Developers](https://developers.facebook.com/), crear una App tipo **Business** y agregar el producto **WhatsApp**.
2. **Día 1–2 — Registrar las 7 plantillas:** En WhatsApp Manager -> *Account tools* -> *Message templates*, registrar los 7 templates exactamente con los nombres y parámetros de la tabla superior, adjuntando valores de ejemplo realistas (sin datos reales de pacientes).
   - Revisión automática de Meta: entre minutos y 48 horas.
   - Si Meta reclasifica alguna plantilla a `MARKETING`, apelar desde WhatsApp Manager explicando que corresponde a notificación transaccional de hora médica agendada (3–5 días hábiles).
3. **Día 1–2 — Configurar Webhook:** En *WhatsApp > Configuration*:
   - **Callback URL:** `https://<tu-api>.onrender.com/api/webhooks/notifications/whatsapp`
   - **Verify Token:** valor secreto configurado en `Notifications__WhatsApp__WebhookVerifyToken`
   - **Subscribed fields:** `messages` (cubre estados `sent`/`delivered`/`read`/`failed` y respuestas/botones entrantes).
4. **Solo para Producción Real (2–7 días hábiles):**
   - Verificación comercial del **Meta Business Portfolio** (documentos tributarios/razón social de la clínica).
   - Número telefónico dedicado (no vinculado a WhatsApp personal/Business app).
   - **System User** en Business Manager con permiso `whatsapp_business_messaging` y token permanente.
   - Medio de pago agregado en WhatsApp Manager para cargos de categoría `UTILITY`.

---

## 4. Sandbox / Demo del Proyecto de Título (Costo USD 0)

Para la defensa y pruebas end-to-end previas a verificación comercial:

1. **Test Phone Number oficial de Meta:** Meta entrega un número de prueba gratuito dentro del panel de la App (*WhatsApp > API Setup*).
2. **Allowlist de hasta 5 números chilenos (`+56 9 ...`):** Agregar tu teléfono, el del equipo y el de la comisión evaluadora mediante verificación por código SMS/WhatsApp.
3. **Flujo real en vivo:**
   - Permite enviar las plantillas pre-aprobadas (o la plantilla estándar `hello_world` mientras se aprueban) por HTTPS real contra Graph API `v21.0`.
   - Recibe webhooks reales (`delivered`, `read` y clicks en botones Quick Reply `Confirmar` / `Cancelar`) en Render HTTPS.
   - **Fallback automático en demo:** Si envías a un número fuera de la allowlist (error Meta `131030` / `131026`), `SendNotificationConsumer` ejecuta automáticamente el fallback a `Resend` Email y deja trazado `canal_original = WhatsApp` -> `canal = Email`.
4. **Desarrollo local y CI:** Mantener `Notifications:EmailProvider = "Fake"` y `Notifications:WhatsAppProvider = "Fake"` para pruebas determinísticas sin consumir cuota ni red externa.

---

## 5. Variables de Entorno Requeridas en Render (Producción / Staging)

```text
Notifications__EmailProvider=Resend
Notifications__WhatsAppProvider=MetaCloud
Notifications__ActionTokenSecret=<secreto-hmac-aleatorio-min-32-chars>
Notifications__PublicBaseUrl=https://<frontend-medcare>
Notifications__Resend__ApiKey=re_...
Notifications__Resend__FromAddress=notificaciones@<dominio-verificado>
Notifications__Resend__WebhookSecret=whsec_...
Notifications__WhatsApp__PhoneNumberId=<id-numero-meta>
Notifications__WhatsApp__AccessToken=<token-system-user-o-test>
Notifications__WhatsApp__AppSecret=<meta-app-secret>
Notifications__WhatsApp__WebhookVerifyToken=<verify-token-secreto>
```
