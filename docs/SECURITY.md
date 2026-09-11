# Seguridad

## Límite del producto

Atlas SoftPLC **no** es: Safety PLC, SIL-rated, controlador certificado de
seguridad funcional, sustituto de E-Stop, sustituto de relés de seguridad, ni
controlador hard-real-time. Windows no es hard-real-time.

## Reglas

- Una variable con `SafetyCritical = true` no se habilita silenciosamente.
- Un E-Stop físico debe cortar energía/control con hardware apropiado. Atlas lo
  supervisa/registra/reacciona por software, pero no lo reemplaza.
- Failsafe por defecto `false` para salidas físicas.

## Seguridad web

- Autenticación local por cookies.
- Roles: `Viewer`, `Operator`, `Engineer`, `Administrator`.
- Antiforgery, HTTPS, validación server-side, encoding, límites de payload.
- Nunca usar GET para acciones (TurnMotorOn, ActivateProgram, ForceOutput).

## Permisos

| Rol | Permisos |
|-----|----------|
| Viewer | ver |
| Operator | operar, ack alarmas, simulación |
| Engineer | configurar, reglas, bindings, validar |
| Administrator | usuarios, seguridad, drivers, activación física |

## Red OT

```
Internet → Firewall → IT Network → Firewall/segmentación → OT Network → Atlas + devices
```

No exponer Modbus TCP directamente a Internet.

## IA

La IA nunca: escribe outputs, conoce credenciales si no es necesario, ejecuta
código, activa programas ni desactiva interlocks. Solo devuelve
`AutomationIntentProposal`, y Atlas valida antes de aplicar.