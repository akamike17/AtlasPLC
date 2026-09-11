# Guía del operador

Atlas SoftPLC está diseñado para que **no necesites saber PLC**. No verás
coils, registros, direcciones ni lenguajes como Ladder o Structured Text.

## Qué puedes hacer

1. **Decir qué quieres controlar** y qué debe hacer. Por ejemplo:
   > "Enciende la bomba cuando el nivel esté bajo y apágala cuando llegue arriba."
2. **Simular** sin hardware real.
3. **Ver la explicación** de por qué algo está encendido o apagado.
4. **Activar** el control físico solo cuando estés listo.

## Pantallas

- **Dashboard**: estado del runtime, entradas, salidas, automatización activa.
- **Simulación**: cambia sensores/interruptores y observa entradas → salidas.
- **Diagnóstico**: tiempos de scan y métricas.

## Activar / pausar

- El arranque **no escribe** a salidas físicas (empieza en estado seguro).
- La activación física la hace un usuario con permiso de Administrator.

## Seguridad

- Un **Paro de emergencia** físico real debe cortar energía con hardware
  apropiado. Atlas lo supervisa y reacciona, pero no lo reemplaza.
- Ante pérdida de comunicación o fallo, las salidas van a su estado seguro
  (normalmente apagadas).

## Preguntas frecuentes

**¿Por qué está encendida la bomba?**
Pulsa "Ver explicación": te muestra la regla y las condiciones que la activan.

**¿Puedo romper algo al cambiar un sensor?**
No en simulación. En modo físico, los interlocks y failsafes protegen los
estados inseguros según lo que configuraste.