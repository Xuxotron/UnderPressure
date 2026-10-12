; Updated: 2026-10-12
option casemap:none
EXTERN resolve_export:PROC
PUBLIC proxy_0
PUBLIC proxy_1
PUBLIC proxy_2
PUBLIC proxy_3
PUBLIC proxy_4
PUBLIC proxy_5
PUBLIC proxy_6
PUBLIC proxy_7
PUBLIC proxy_8
PUBLIC proxy_9
PUBLIC proxy_10
PUBLIC proxy_11
PUBLIC proxy_12
PUBLIC proxy_13
PUBLIC proxy_14
PUBLIC proxy_15
PUBLIC proxy_16
PUBLIC proxy_17
PUBLIC proxy_18
PUBLIC proxy_19
PUBLIC proxy_20
PUBLIC proxy_21
PUBLIC proxy_22
PUBLIC proxy_23
PUBLIC proxy_24
PUBLIC proxy_25
PUBLIC proxy_26
PUBLIC proxy_27
PUBLIC proxy_28
PUBLIC proxy_29
PUBLIC proxy_30
PUBLIC proxy_31
PUBLIC proxy_32
PUBLIC proxy_33
PUBLIC proxy_34
PUBLIC proxy_35
PUBLIC proxy_36
PUBLIC proxy_37
PUBLIC proxy_38
PUBLIC proxy_39
PUBLIC proxy_40
PUBLIC proxy_41
PUBLIC proxy_42
PUBLIC proxy_43
PUBLIC proxy_44
PUBLIC proxy_45
PUBLIC proxy_46
PUBLIC proxy_47
PUBLIC proxy_48
PUBLIC proxy_49
PUBLIC proxy_50
PUBLIC proxy_51
PUBLIC proxy_52
PUBLIC proxy_53
PUBLIC proxy_54
PUBLIC proxy_55
PUBLIC proxy_56
PUBLIC proxy_57
PUBLIC proxy_58
PUBLIC proxy_59
PUBLIC proxy_60
PUBLIC proxy_61
PUBLIC proxy_62
PUBLIC proxy_63
PUBLIC proxy_64
PUBLIC proxy_65
PUBLIC proxy_66
PUBLIC proxy_67
PUBLIC proxy_68
PUBLIC proxy_69
PUBLIC proxy_70
PUBLIC proxy_71
PUBLIC proxy_72
PUBLIC proxy_73
PUBLIC proxy_74
PUBLIC proxy_75
PUBLIC proxy_76
PUBLIC proxy_77
PUBLIC proxy_78
PUBLIC proxy_79
PUBLIC proxy_80
PUBLIC proxy_81
PUBLIC proxy_82
PUBLIC proxy_83
PUBLIC proxy_84
PUBLIC proxy_85
PUBLIC proxy_86
PUBLIC proxy_87
PUBLIC proxy_88
PUBLIC proxy_89
PUBLIC proxy_90
PUBLIC proxy_91
.code

proxy_0 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 0
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_0 ENDP

proxy_1 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 1
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_1 ENDP

proxy_2 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 2
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_2 ENDP

proxy_3 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 3
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_3 ENDP

proxy_4 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 4
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_4 ENDP

proxy_5 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 5
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_5 ENDP

proxy_6 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 6
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_6 ENDP

proxy_7 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 7
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_7 ENDP

proxy_8 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 8
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_8 ENDP

proxy_9 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 9
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_9 ENDP

proxy_10 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 10
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_10 ENDP

proxy_11 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 11
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_11 ENDP

proxy_12 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 12
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_12 ENDP

proxy_13 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 13
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_13 ENDP

proxy_14 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 14
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_14 ENDP

proxy_15 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 15
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_15 ENDP

proxy_16 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 16
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_16 ENDP

proxy_17 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 17
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_17 ENDP

proxy_18 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 18
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_18 ENDP

proxy_19 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 19
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_19 ENDP

proxy_20 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 20
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_20 ENDP

proxy_21 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 21
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_21 ENDP

proxy_22 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 22
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_22 ENDP

proxy_23 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 23
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_23 ENDP

proxy_24 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 24
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_24 ENDP

proxy_25 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 25
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_25 ENDP

proxy_26 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 26
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_26 ENDP

proxy_27 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 27
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_27 ENDP

proxy_28 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 28
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_28 ENDP

proxy_29 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 29
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_29 ENDP

proxy_30 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 30
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_30 ENDP

proxy_31 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 31
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_31 ENDP

proxy_32 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 32
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_32 ENDP

proxy_33 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 33
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_33 ENDP

proxy_34 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 34
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_34 ENDP

proxy_35 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 35
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_35 ENDP

proxy_36 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 36
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_36 ENDP

proxy_37 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 37
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_37 ENDP

proxy_38 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 38
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_38 ENDP

proxy_39 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 39
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_39 ENDP

proxy_40 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 40
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_40 ENDP

proxy_41 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 41
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_41 ENDP

proxy_42 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 42
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_42 ENDP

proxy_43 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 43
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_43 ENDP

proxy_44 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 44
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_44 ENDP

proxy_45 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 45
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_45 ENDP

proxy_46 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 46
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_46 ENDP

proxy_47 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 47
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_47 ENDP

proxy_48 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 48
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_48 ENDP

proxy_49 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 49
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_49 ENDP

proxy_50 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 50
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_50 ENDP

proxy_51 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 51
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_51 ENDP

proxy_52 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 52
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_52 ENDP

proxy_53 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 53
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_53 ENDP

proxy_54 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 54
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_54 ENDP

proxy_55 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 55
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_55 ENDP

proxy_56 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 56
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_56 ENDP

proxy_57 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 57
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_57 ENDP

proxy_58 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 58
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_58 ENDP

proxy_59 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 59
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_59 ENDP

proxy_60 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 60
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_60 ENDP

proxy_61 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 61
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_61 ENDP

proxy_62 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 62
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_62 ENDP

proxy_63 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 63
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_63 ENDP

proxy_64 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 64
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_64 ENDP

proxy_65 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 65
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_65 ENDP

proxy_66 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 66
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_66 ENDP

proxy_67 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 67
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_67 ENDP

proxy_68 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 68
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_68 ENDP

proxy_69 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 69
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_69 ENDP

proxy_70 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 70
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_70 ENDP

proxy_71 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 71
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_71 ENDP

proxy_72 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 72
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_72 ENDP

proxy_73 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 73
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_73 ENDP

proxy_74 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 74
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_74 ENDP

proxy_75 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 75
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_75 ENDP

proxy_76 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 76
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_76 ENDP

proxy_77 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 77
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_77 ENDP

proxy_78 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 78
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_78 ENDP

proxy_79 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 79
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_79 ENDP

proxy_80 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 80
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_80 ENDP

proxy_81 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 81
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_81 ENDP

proxy_82 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 82
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_82 ENDP

proxy_83 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 83
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_83 ENDP

proxy_84 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 84
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_84 ENDP

proxy_85 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 85
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_85 ENDP

proxy_86 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 86
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_86 ENDP

proxy_87 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 87
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_87 ENDP

proxy_88 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 88
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_88 ENDP

proxy_89 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 89
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_89 ENDP

proxy_90 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 90
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_90 ENDP

proxy_91 PROC
    sub rsp, 0B8h
    mov QWORD PTR [rsp+20h], rcx
    mov QWORD PTR [rsp+28h], rdx
    mov QWORD PTR [rsp+30h], r8
    mov QWORD PTR [rsp+38h], r9
    movdqu XMMWORD PTR [rsp+40h], xmm0
    movdqu XMMWORD PTR [rsp+50h], xmm1
    movdqu XMMWORD PTR [rsp+60h], xmm2
    movdqu XMMWORD PTR [rsp+70h], xmm3
    mov ecx, 91
    call resolve_export
    movdqu xmm0, XMMWORD PTR [rsp+40h]
    movdqu xmm1, XMMWORD PTR [rsp+50h]
    movdqu xmm2, XMMWORD PTR [rsp+60h]
    movdqu xmm3, XMMWORD PTR [rsp+70h]
    mov rcx, QWORD PTR [rsp+20h]
    mov rdx, QWORD PTR [rsp+28h]
    mov r8, QWORD PTR [rsp+30h]
    mov r9, QWORD PTR [rsp+38h]
    add rsp, 0B8h
    jmp rax
proxy_91 ENDP

END
